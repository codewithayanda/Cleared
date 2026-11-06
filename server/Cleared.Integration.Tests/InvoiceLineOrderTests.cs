using System.Net.Http.Json;
using Cleared.Application.Invoices;

namespace Cleared.Integration.Tests;

// The database gives no order of its own, and a line's id is a random Guid, so an invoice read back used to show its
// lines in the order of those ids. These use enough lines that landing in the right order by chance is next to
// impossible, and read them every way an invoice is read.
[Collection(ApiCollection.Name)]
public class InvoiceLineOrderTests(ClearedApiFactory factory)
{
    private const int LineCount = 8;

    private static readonly string[] Names = [.. Enumerable.Range(1, LineCount).Select(n => $"Item number {n}")];

    [Fact]
    public async Task A_new_invoice_reads_back_with_its_lines_in_the_order_they_were_entered()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");

        var created = await owner.Client.PostJsonAsync<InvoiceResponse>(
            "api/v1/invoices", new CreateInvoiceRequest(customer.Id, LinesNamed(Names)));

        Assert.Equal(Names, Descriptions(created));
        await AssertEveryReadShowsAsync(owner, created.Id, Names);
    }

    [Fact]
    public async Task Issuing_does_not_change_the_order_of_the_lines()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");
        var draft = await owner.Client.PostJsonAsync<InvoiceResponse>(
            "api/v1/invoices", new CreateInvoiceRequest(customer.Id, LinesNamed(Names)));

        var issued = await owner.Client.PostJsonAsync<InvoiceResponse>(
            $"api/v1/invoices/{draft.Id}/issue", TestData.IssueToday());

        Assert.Equal(Names, Descriptions(issued));
        await AssertEveryReadShowsAsync(owner, issued.Id, Names);
    }

    [Fact]
    public async Task A_changed_draft_keeps_the_order_the_lines_were_sent_in()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");
        var draft = await owner.Client.PostJsonAsync<InvoiceResponse>(
            "api/v1/invoices", new CreateInvoiceRequest(customer.Id, LinesNamed(Names)));
        var reversed = Names.Reverse().ToArray();

        using var response = await owner.Client.PutAsJsonAsync(
            $"api/v1/invoices/{draft.Id}", new UpdateInvoiceRequest(customer.Id, LinesNamed(reversed)), ApiJson.Options);
        response.EnsureSuccessStatusCode();

        await AssertEveryReadShowsAsync(owner, draft.Id, reversed);
    }

    [Fact]
    public async Task Saving_a_draft_again_without_changes_leaves_its_lines_where_they_were()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");
        var draft = await owner.Client.PostJsonAsync<InvoiceResponse>(
            "api/v1/invoices", new CreateInvoiceRequest(customer.Id, LinesNamed(Names)));

        // What the edit form does: open the draft as it is read, and send it back as it was opened.
        for (var save = 0; save < 3; save++)
        {
            var opened = await owner.Client.GetFromJsonAsync<InvoiceResponse>(
                $"api/v1/invoices/{draft.Id}", ApiJson.Options);
            var sent = opened!.Lines
                .Select(line => new CreateInvoiceLineRequest(line.Description, line.Quantity, line.UnitPrice))
                .ToList();

            using var response = await owner.Client.PutAsJsonAsync(
                $"api/v1/invoices/{draft.Id}", new UpdateInvoiceRequest(customer.Id, sent), ApiJson.Options);
            response.EnsureSuccessStatusCode();
        }

        await AssertEveryReadShowsAsync(owner, draft.Id, Names);
    }

    private static List<CreateInvoiceLineRequest> LinesNamed(IEnumerable<string> names) =>
        [.. names.Select(name => new CreateInvoiceLineRequest(name, 1m, "10.00"))];

    private static string[] Descriptions(InvoiceResponse invoice) => [.. invoice.Lines.Select(line => line.Description)];

    // By id, in the list, and on the PDF the owner sends to a customer, where the order is what a customer reads.
    private static async Task AssertEveryReadShowsAsync(TestTenant owner, Guid invoiceId, string[] expected)
    {
        var fetched = await owner.Client.GetFromJsonAsync<InvoiceResponse>($"api/v1/invoices/{invoiceId}", ApiJson.Options);
        Assert.Equal(expected, Descriptions(fetched!));

        var listed = await owner.Client.GetFromJsonAsync<List<InvoiceResponse>>("api/v1/invoices", ApiJson.Options);
        Assert.Equal(expected, Descriptions(Assert.Single(listed!, invoice => invoice.Id == invoiceId)));

        var text = PdfText.Of(await owner.Client.GetByteArrayAsync($"api/v1/invoices/{invoiceId}/pdf"));
        var places = expected.Select(name => text.IndexOf(name, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, places);
        Assert.Equal(places.Order(), places);
    }
}
