using System.Net;
using System.Net.Http.Json;
using Cleared.Application.Invoices;
using Cleared.Application.Payments;

namespace Cleared.Integration.Tests;

// A client typo is the client's problem. It must come back as a 400, never a 500.
[Collection(ApiCollection.Name)]
public class MalformedMoneyTests(ClearedApiFactory factory)
{
    [Theory]
    [InlineData("abc")]
    [InlineData("1,50")]
    [InlineData("")]
    [InlineData("99999999999999999999999999999")]
    public async Task A_malformed_unit_price_is_a_400(string unitPrice)
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Typo Customer");
        var request = new CreateInvoiceRequest(customer.Id, [new CreateInvoiceLineRequest("Consulting", 1, unitPrice)]);

        var response = await owner.Client.PostAsJsonAsync("api/v1/invoices", request, ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1,50")]
    [InlineData("99999999999999999999999999999")]
    public async Task A_malformed_payment_amount_is_a_400(string amount)
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Typo Customer");
        var invoice = await owner.Client.CreateIssuedInvoiceAsync(customer.Id);

        var response = await owner.Client.PostAsJsonAsync(
            $"api/v1/invoices/{invoice.Id}/payments",
            new RecordPaymentRequest(amount, TestData.Today, null),
            ApiJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
