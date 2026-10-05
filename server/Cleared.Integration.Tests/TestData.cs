using System.Net.Http.Json;
using System.Text.Json;
using Cleared.Application.Customers;
using Cleared.Application.Invoices;

namespace Cleared.Integration.Tests;

public static class TestData
{
    public static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public static CreateInvoiceRequest NewInvoice(Guid customerId) =>
        new(customerId, [new CreateInvoiceLineRequest("Consulting", 2, "500.00")]);

    public static IssueInvoiceRequest IssueToday() => new(Today, Today.AddDays(30));

    // Setup calls fail with the response body, not just a status code.
    public static async Task<T> PostJsonAsync<T>(this HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body, ApiJson.Options);
        var text = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"POST {url} returned {(int)response.StatusCode}: {text}");
        }

        return JsonSerializer.Deserialize<T>(text, ApiJson.Options)!;
    }

    public static Task<CustomerResponse> CreateCustomerAsync(this HttpClient client, string name) =>
        client.PostJsonAsync<CustomerResponse>(
            "api/v1/customers", new CreateCustomerRequest(name, null, "ap@example.test", "1 Main Road, Cape Town"));

    public static async Task<InvoiceResponse> CreateIssuedInvoiceAsync(this HttpClient client, Guid customerId)
    {
        var draft = await client.PostJsonAsync<InvoiceResponse>("api/v1/invoices", NewInvoice(customerId));

        return await client.PostJsonAsync<InvoiceResponse>($"api/v1/invoices/{draft.Id}/issue", IssueToday());
    }
}
