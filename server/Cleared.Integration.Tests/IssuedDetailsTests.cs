using System.Net.Http.Json;
using Cleared.Application.Customers;
using Cleared.Application.Invoices;
using Cleared.Application.Tenants;

namespace Cleared.Integration.Tests;

// What an invoice says about who it is from and to is fixed on the day it is issued. These read the PDF the
// API sends after the customer and the company have changed, so they prove it for the real page.
[Collection(ApiCollection.Name)]
public class IssuedDetailsTests(ClearedApiFactory factory)
{
    [Fact]
    public async Task An_issued_invoice_keeps_its_details_when_the_customer_and_the_company_change()
    {
        var owner = await factory.CreateTenantAsync();
        await SetCompanyAsync(owner, "1 Acme Way, Cape Town", "FNB", "62123456789", "250655");
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");
        var invoice = await owner.Client.CreateIssuedInvoiceAsync(customer.Id);

        await ChangeCustomerAsync(owner, customer.Id);
        await SetCompanyAsync(owner, "5 New Road, Pretoria", "Nedbank", "99887766", "198765");
        var text = await PdfTextAsync(owner, invoice);

        Assert.Contains("Beta Co", text);
        Assert.Contains("1 Main Road, Cape Town", text);
        Assert.DoesNotContain("Gamma Ltd", text);
        Assert.DoesNotContain("9 High Street", text);
        Assert.Contains("1 Acme Way, Cape Town", text);
        Assert.DoesNotContain("5 New Road", text);
    }

    [Fact]
    public async Task The_bank_details_on_an_issued_invoice_are_the_ones_the_company_has_now()
    {
        var owner = await factory.CreateTenantAsync();
        await SetCompanyAsync(owner, "1 Acme Way, Cape Town", "FNB", "62123456789", "250655");
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");
        var invoice = await owner.Client.CreateIssuedInvoiceAsync(customer.Id);

        await SetCompanyAsync(owner, "5 New Road, Pretoria", "Nedbank", "99887766", "198765");
        var text = await PdfTextAsync(owner, invoice);

        Assert.Contains("Nedbank", text);
        Assert.Contains("Account number: 99887766", text);
        Assert.DoesNotContain("62123456789", text);
    }

    [Fact]
    public async Task A_draft_shows_the_details_the_customer_has_now()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");
        var draft = await owner.Client.PostJsonAsync<InvoiceResponse>("api/v1/invoices", TestData.NewInvoice(customer.Id));

        await ChangeCustomerAsync(owner, customer.Id);
        var text = await PdfTextAsync(owner, draft);

        Assert.Contains("Gamma Ltd", text);
        Assert.Contains("9 High Street", text);
        Assert.DoesNotContain("Beta Co", text);
    }

    private static async Task ChangeCustomerAsync(TestTenant owner, Guid customerId)
    {
        using var response = await owner.Client.PutAsJsonAsync(
            $"api/v1/customers/{customerId}",
            new UpdateCustomerRequest("Gamma Ltd", null, "ap@example.test", "9 High Street, Durban"),
            ApiJson.Options);
        response.EnsureSuccessStatusCode();
    }

    private static async Task SetCompanyAsync(
        TestTenant owner, string address, string bank, string accountNumber, string branchCode)
    {
        using var response = await owner.Client.PutAsJsonAsync(
            "api/v1/tenants/me", new UpdateTenantProfileRequest(address, bank, accountNumber, branchCode), ApiJson.Options);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> PdfTextAsync(TestTenant owner, InvoiceResponse invoice) =>
        PdfText.Of(await owner.Client.GetByteArrayAsync($"api/v1/invoices/{invoice.Id}/pdf"));
}
