using System.Net;
using System.Net.Http.Json;
using Cleared.Application.Auditing;
using Cleared.Application.Invoices;

namespace Cleared.Integration.Tests;

// Only a draft can be changed or deleted. An invoice that has been issued is part of the record, and is
// corrected with a credit note instead.
[Collection(ApiCollection.Name)]
public class DraftEditTests(ClearedApiFactory factory)
{
    [Fact]
    public async Task Changing_a_draft_replaces_its_customer_and_lines_and_every_read_shows_it()
    {
        var owner = await factory.CreateTenantAsync();
        var first = await owner.Client.CreateCustomerAsync("Beta Co");
        var second = await owner.Client.CreateCustomerAsync("Gamma Ltd");
        var draft = await NewDraftAsync(owner, first.Id);
        var request = new UpdateInvoiceRequest(
            second.Id,
            [
                new CreateInvoiceLineRequest("Design", 3m, "100.00"),
                new CreateInvoiceLineRequest("Hosting", 1m, "50.00"),
            ]);

        using var response = await owner.Client.PutAsJsonAsync($"api/v1/invoices/{draft.Id}", request, ApiJson.Options);
        var changed = await response.Content.ReadFromJsonAsync<InvoiceResponse>(ApiJson.Options);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(draft.Id, changed!.Id);
        Assert.Equal("Draft", changed.Status);
        Assert.Equal(second.Id, changed.CustomerId);
        Assert.Equal(["Design", "Hosting"], changed.Lines.Select(line => line.Description));
        Assert.Equal("350.00", changed.Total);

        var fetched = await owner.Client.GetFromJsonAsync<InvoiceResponse>($"api/v1/invoices/{draft.Id}", ApiJson.Options);
        Assert.Equal(second.Id, fetched!.CustomerId);
        Assert.Equal(["Design", "Hosting"], fetched.Lines.Select(line => line.Description));
        Assert.Equal("350.00", fetched.Total);
        var listed = await owner.Client.GetFromJsonAsync<List<InvoiceResponse>>("api/v1/invoices", ApiJson.Options);
        Assert.Equal(second.Id, Assert.Single(listed!).CustomerId);
    }

    [Fact]
    public async Task A_draft_that_was_changed_is_issued_to_the_customer_it_was_moved_to()
    {
        var owner = await factory.CreateTenantAsync();
        var first = await owner.Client.CreateCustomerAsync("Beta Co");
        var second = await owner.Client.CreateCustomerAsync("Gamma Ltd");
        var draft = await NewDraftAsync(owner, first.Id);
        using var change = await owner.Client.PutAsJsonAsync(
            $"api/v1/invoices/{draft.Id}", ChangeTo(second.Id), ApiJson.Options);
        change.EnsureSuccessStatusCode();

        var issued = await owner.Client.PostJsonAsync<InvoiceResponse>(
            $"api/v1/invoices/{draft.Id}/issue", TestData.IssueToday());
        var text = PdfText.Of(await owner.Client.GetByteArrayAsync($"api/v1/invoices/{issued.Id}/pdf"));

        Assert.Equal(second.Id, issued.CustomerId);
        Assert.Contains("Gamma Ltd", text);
        Assert.DoesNotContain("Beta Co", text);
        Assert.Contains("Changed", text);
        Assert.DoesNotContain("Consulting", text);
    }

    [Fact]
    public async Task A_draft_that_does_not_exist_is_not_found_to_change_or_to_delete()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");
        var missing = $"api/v1/invoices/{Guid.NewGuid()}";

        using var change = await owner.Client.PutAsJsonAsync(missing, ChangeTo(customer.Id), ApiJson.Options);
        using var delete = await owner.Client.DeleteAsync(missing);

        Assert.Equal(HttpStatusCode.NotFound, change.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task A_draft_cannot_be_moved_to_another_tenants_customer_and_nothing_changes()
    {
        var owner = await factory.CreateTenantAsync();
        var other = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");
        var othersCustomer = await other.Client.CreateCustomerAsync("Not Yours");
        var draft = await NewDraftAsync(owner, customer.Id);

        using var response = await owner.Client.PutAsJsonAsync(
            $"api/v1/invoices/{draft.Id}", ChangeTo(othersCustomer.Id), ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUnchangedAsync(owner, draft.Id, customer.Id);
    }

    [Fact]
    public async Task An_issued_invoice_cannot_be_changed_or_deleted_and_stays_as_it_was()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");
        var issued = await owner.Client.CreateIssuedInvoiceAsync(customer.Id);

        using var change = await owner.Client.PutAsJsonAsync(
            $"api/v1/invoices/{issued.Id}", ChangeTo(customer.Id), ApiJson.Options);
        using var delete = await owner.Client.DeleteAsync($"api/v1/invoices/{issued.Id}");

        Assert.Equal(HttpStatusCode.Conflict, change.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        var after = await owner.Client.GetFromJsonAsync<InvoiceResponse>($"api/v1/invoices/{issued.Id}", ApiJson.Options);
        Assert.Equal("Issued", after!.Status);
        Assert.Equal(issued.Number, after.Number);
        Assert.Equal("Consulting", Assert.Single(after.Lines).Description);
        var audit = await owner.Client.GetFromJsonAsync<List<AuditLogResponse>>("api/v1/audit", ApiJson.Options);
        Assert.DoesNotContain(audit!, entry => entry.Action == "Deleted");
    }

    [Theory]
    [InlineData("Item", 0, "10.00")]
    [InlineData("Item", -1, "10.00")]
    [InlineData("", 1, "10.00")]
    [InlineData("Item", 1, "ten rand")]
    public async Task A_line_that_cannot_be_right_is_refused_and_nothing_changes(
        string description, int quantity, string unitPrice)
    {
        var owner = await factory.CreateTenantAsync();
        var first = await owner.Client.CreateCustomerAsync("Beta Co");
        var second = await owner.Client.CreateCustomerAsync("Gamma Ltd");
        var draft = await NewDraftAsync(owner, first.Id);
        var request = new UpdateInvoiceRequest(
            second.Id,
            [
                new CreateInvoiceLineRequest("Fine", 1m, "10.00"),
                new CreateInvoiceLineRequest(description, quantity, unitPrice),
            ]);

        using var response = await owner.Client.PutAsJsonAsync($"api/v1/invoices/{draft.Id}", request, ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUnchangedAsync(owner, draft.Id, first.Id);
    }

    [Fact]
    public async Task A_change_that_leaves_out_the_lines_is_refused_and_nothing_changes()
    {
        var owner = await factory.CreateTenantAsync();
        var first = await owner.Client.CreateCustomerAsync("Beta Co");
        var second = await owner.Client.CreateCustomerAsync("Gamma Ltd");
        var draft = await NewDraftAsync(owner, first.Id);

        using var response = await owner.Client.PutAsJsonAsync(
            $"api/v1/invoices/{draft.Id}", new { customerId = second.Id }, ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUnchangedAsync(owner, draft.Id, first.Id);
    }

    [Fact]
    public async Task Deleting_a_draft_removes_it_and_leaves_an_audit_entry()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");
        var draft = await NewDraftAsync(owner, customer.Id);
        var kept = await NewDraftAsync(owner, customer.Id);

        using var response = await owner.Client.DeleteAsync($"api/v1/invoices/{draft.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var read = await owner.Client.GetAsync($"api/v1/invoices/{draft.Id}");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        var listed = await owner.Client.GetFromJsonAsync<List<InvoiceResponse>>("api/v1/invoices", ApiJson.Options);
        Assert.Equal(kept.Id, Assert.Single(listed!).Id);
        var audit = await owner.Client.GetFromJsonAsync<List<AuditLogResponse>>("api/v1/audit", ApiJson.Options);
        var entry = Assert.Single(audit!, e => e.Action == "Deleted");
        Assert.Equal("Invoice", entry.EntityType);
        Assert.Equal(draft.Id, entry.EntityId);
    }

    [Fact]
    public async Task Deleting_a_draft_twice_finds_it_gone_the_second_time()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");
        var draft = await NewDraftAsync(owner, customer.Id);

        using var first = await owner.Client.DeleteAsync($"api/v1/invoices/{draft.Id}");
        using var second = await owner.Client.DeleteAsync($"api/v1/invoices/{draft.Id}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        var audit = await owner.Client.GetFromJsonAsync<List<AuditLogResponse>>("api/v1/audit", ApiJson.Options);
        Assert.Single(audit!, entry => entry.Action == "Deleted");
    }

    [Fact]
    public async Task Deleting_a_draft_leaves_no_gap_in_the_invoice_numbers()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");
        var removed = await NewDraftAsync(owner, customer.Id);
        var kept = await NewDraftAsync(owner, customer.Id);
        using var delete = await owner.Client.DeleteAsync($"api/v1/invoices/{removed.Id}");
        delete.EnsureSuccessStatusCode();

        var issued = await owner.Client.PostJsonAsync<InvoiceResponse>(
            $"api/v1/invoices/{kept.Id}/issue", TestData.IssueToday());

        Assert.Equal($"INV-{TestData.Today.Year}-0001", issued.Number);
    }

    private static Task<InvoiceResponse> NewDraftAsync(TestTenant owner, Guid customerId) =>
        owner.Client.PostJsonAsync<InvoiceResponse>("api/v1/invoices", TestData.NewInvoice(customerId));

    private static UpdateInvoiceRequest ChangeTo(Guid customerId) =>
        new(customerId, [new CreateInvoiceLineRequest("Changed", 1m, "10.00")]);

    // The draft is still the one that TestData.NewInvoice made: its customer, and its one line.
    private static async Task AssertUnchangedAsync(TestTenant owner, Guid invoiceId, Guid customerId)
    {
        var after = await owner.Client.GetFromJsonAsync<InvoiceResponse>($"api/v1/invoices/{invoiceId}", ApiJson.Options);

        Assert.Equal(customerId, after!.CustomerId);
        var line = Assert.Single(after.Lines);
        Assert.Equal("Consulting", line.Description);
        Assert.Equal(2m, line.Quantity);
    }
}
