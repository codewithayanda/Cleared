using System.Net.Http.Json;
using System.Security.Claims;
using Cleared.Application.Abstractions;
using Cleared.Application.Invoices;
using Cleared.Domain.Invoicing;
using Cleared.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Cleared.Integration.Tests;

// The list is ordered by the database, so these run against a real one: newest first, and the id settles
// a tie so the same call always gives the same order.
[Collection(ApiCollection.Name)]
public class InvoiceOrderTests(ClearedApiFactory factory)
{
    [Fact]
    public async Task The_list_shows_the_newest_invoice_first()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");
        var first = await CreateDraftAsync(owner, customer.Id);
        var second = await CreateDraftAsync(owner, customer.Id);
        var third = await CreateDraftAsync(owner, customer.Id);

        var listed = await ListThroughTheApiAsync(owner);

        Assert.Equal([third.Id, second.Id, first.Id], listed.Select(invoice => invoice.Id));
    }

    [Fact]
    public async Task Issuing_an_invoice_does_not_move_it_in_the_list()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Beta Co");
        var first = await CreateDraftAsync(owner, customer.Id);
        var second = await CreateDraftAsync(owner, customer.Id);
        await owner.Client.PostJsonAsync<InvoiceResponse>($"api/v1/invoices/{first.Id}/issue", TestData.IssueToday());

        var listed = await ListThroughTheApiAsync(owner);

        Assert.Equal([second.Id, first.Id], listed.Select(invoice => invoice.Id));
    }

    [Fact]
    public async Task Invoices_made_at_the_same_moment_are_listed_by_id_whatever_order_they_were_saved_in()
    {
        var owner = await factory.CreateTenantAsync();
        var moment = new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() }.Order().ToArray();
        await SaveOneByOneAsync(owner.TenantId, ids.Select(id => Invoice.CreateDraft(id, owner.TenantId, Guid.NewGuid(), moment)));

        var listed = await ListFromTheRepositoryAsync(owner.TenantId);

        Assert.Equal(ids.Reverse(), listed.Select(invoice => invoice.Id));
    }

    private static Task<InvoiceResponse> CreateDraftAsync(TestTenant owner, Guid customerId) =>
        owner.Client.PostJsonAsync<InvoiceResponse>("api/v1/invoices", TestData.NewInvoice(customerId));

    private static async Task<List<InvoiceResponse>> ListThroughTheApiAsync(TestTenant owner) =>
        (await owner.Client.GetFromJsonAsync<List<InvoiceResponse>>("api/v1/invoices", ApiJson.Options))!;

    // Saved one at a time and in id order, so the order the rows were stored in is the opposite of the
    // order the list must give.
    private async Task SaveOneByOneAsync(Guid tenantId, IEnumerable<Invoice> invoices)
    {
        using var scope = ScopeFor(tenantId);
        var dbContext = scope.ServiceProvider.GetRequiredService<ClearedDbContext>();

        foreach (var invoice in invoices)
        {
            dbContext.Invoices.Add(invoice);
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task<IReadOnlyList<Invoice>> ListFromTheRepositoryAsync(Guid tenantId)
    {
        using var scope = ScopeFor(tenantId);
        var invoices = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();

        return await invoices.ListAsync(tenantId, CancellationToken.None);
    }

    // The context the app uses, on a request whose token names the tenant.
    private IServiceScope ScopeFor(Guid tenantId)
    {
        var scope = factory.Services.CreateScope();
        var identity = new ClaimsIdentity([new Claim("tenant_id", tenantId.ToString())], "Test");
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        return scope;
    }
}
