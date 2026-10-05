using System.Net.Http.Json;
using Cleared.Application.CreditNotes;
using Cleared.Application.Customers;
using Cleared.Application.Payments;
using Cleared.Application.Tenants;

namespace Cleared.Integration.Tests;

// What the intruder must get back for a request that touches the owner's data.
public enum Blocked
{
    // 404, the same answer as for an id that does not exist. Never 403.
    NotFound,

    // 400, the body names a row the intruder cannot see.
    BadRequest,

    // 200 and an empty list.
    EmptyList,

    // 2xx, but the response and the effect are the intruder's own.
    OwnTenantOnly,
}

// One entry per tenant-scoped endpoint. Route is the template exactly as the routing table
// spells it, so EndpointGuardTests can match the two. RouteId fills its single placeholder.
public sealed record IsolationCase(
    HttpMethod Method,
    string Route,
    Blocked Intruder,
    Func<IsolationWorld, Guid>? RouteId = null,
    Func<IsolationWorld, object>? Body = null)
{
    public string Endpoint => $"{Method.Method} {Route}";

    public HttpRequestMessage CreateRequest(IsolationWorld world)
    {
        var url = RouteId is null ? Route : FillPlaceholder(Route, RouteId(world));
        var request = new HttpRequestMessage(Method, url);

        if (Body is not null)
        {
            var body = Body(world);
            request.Content = JsonContent.Create(body, body.GetType(), options: ApiJson.Options);
        }

        return request;
    }

    private static string FillPlaceholder(string route, Guid id)
    {
        var open = route.IndexOf('{');
        var close = route.IndexOf('}');

        return string.Concat(route.AsSpan(0, open), id.ToString(), route.AsSpan(close + 1));
    }
}

public static class IsolationCases
{
    public static readonly IReadOnlyList<IsolationCase> All =
    [
        new(HttpMethod.Get, "api/v1/audit", Blocked.EmptyList),
        new(HttpMethod.Get, "api/v1/customers", Blocked.EmptyList),
        new(
            HttpMethod.Post, "api/v1/customers", Blocked.OwnTenantOnly,
            Body: _ => new CreateCustomerRequest("Intruder Co", null, null, null)),
        new(HttpMethod.Get, "api/v1/invoices", Blocked.EmptyList),
        new(
            HttpMethod.Post, "api/v1/invoices", Blocked.BadRequest,
            Body: world => TestData.NewInvoice(world.Customer.Id)),
        new(HttpMethod.Get, "api/v1/invoices/{id:guid}", Blocked.NotFound, world => world.IssuedInvoice.Id),
        new(
            HttpMethod.Post, "api/v1/invoices/{id:guid}/issue", Blocked.NotFound,
            world => world.DraftInvoice.Id, _ => TestData.IssueToday()),
        new(HttpMethod.Get, "api/v1/invoices/{id:guid}/pdf", Blocked.NotFound, world => world.IssuedInvoice.Id),
        new(
            HttpMethod.Get, "api/v1/invoices/{invoiceId:guid}/payments", Blocked.NotFound,
            world => world.IssuedInvoice.Id),
        new(
            HttpMethod.Post, "api/v1/invoices/{invoiceId:guid}/payments", Blocked.NotFound,
            world => world.IssuedInvoice.Id, _ => new RecordPaymentRequest("50.00", TestData.Today, "attack")),
        new(
            HttpMethod.Post, "api/v1/invoices/{invoiceId:guid}/credit-notes", Blocked.NotFound,
            world => world.IssuedInvoice.Id,
            world => new CreateCreditNoteRequest(
                "attack", [new CreateCreditNoteLineRequest(world.IssuedInvoice.Lines[0].Id, 1)])),
        new(HttpMethod.Get, "api/v1/tenants/me", Blocked.OwnTenantOnly),
        new(
            HttpMethod.Put, "api/v1/tenants/me", Blocked.OwnTenantOnly,
            Body: _ => new UpdateTenantProfileRequest("1 Intruder Street", "Intruder Bank", "000111", "222333")),
    ];
}
