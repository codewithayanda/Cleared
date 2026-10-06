using System.Security.Claims;
using Cleared.API.Middleware;
using Cleared.Application.Abstractions;
using Cleared.Domain.Auditing;
using Cleared.Domain.Common;
using Cleared.Domain.Invoicing;
using Cleared.Domain.Payments;
using Cleared.Domain.Tenancy;
using Cleared.Infrastructure.Identity;
using Cleared.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cleared.Integration.Tests;

// Reads are filtered by tenant and writes are verified by TenantWriteInterceptor. Nothing in the
// API can save another tenant's row today, so most of these push rows at a context directly.
[Collection(ApiCollection.Name)]
public class TenantWriteInterceptorTests(ClearedApiFactory factory)
{
    private static readonly Dictionary<string, Func<Guid, object>> Smuggled = new()
    {
        ["Customer"] = tenantId => Customer.Create(Guid.NewGuid(), tenantId, "Smuggled", DateTimeOffset.UtcNow),
        ["Invoice"] = tenantId => Invoice.CreateDraft(Guid.NewGuid(), tenantId, Guid.NewGuid()),
        ["CreditNote"] = tenantId => CreditNote.Create(
            Guid.NewGuid(), tenantId, Guid.NewGuid(), "CN-2026-0001", "Smuggled", TestData.Today, 0.15m,
            [new CreditNoteLineRequest(Guid.NewGuid(), "Item", 1m, Money.Zar(10m), VatTreatment.NotApplicable)]),
        ["Payment"] = tenantId => Payment.RecordManual(
            Guid.NewGuid(), tenantId, Guid.NewGuid(), Money.Zar(10m), TestData.Today, DateTimeOffset.UtcNow),
        ["AuditLog"] = tenantId => AuditLog.Record(
            Guid.NewGuid(), tenantId, Guid.NewGuid(), "Invoice", Guid.NewGuid(), "Smuggled", DateTimeOffset.UtcNow),
    };

    public static IEnumerable<object[]> Kinds() => Smuggled.Keys.Select(kind => new object[] { kind });

    // The registration in Program.cs: the scoped context, the real HttpTenantContext, and a
    // request whose token names the intruder. A forgotten AddInterceptors fails here.
    [Fact]
    public async Task The_context_the_app_uses_refuses_to_save_another_tenants_row()
    {
        var world = await IsolationWorld.CreateAsync(factory);
        var smuggled = Customer.Create(Guid.NewGuid(), world.Owner.TenantId, "Smuggled", DateTimeOffset.UtcNow);

        using (var scope = ScopeFor(world.Intruder.TenantId))
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ClearedDbContext>();
            dbContext.Customers.Add(smuggled);

            var exception = await Assert.ThrowsAsync<TenantMismatchException>(() => dbContext.SaveChangesAsync());

            Assert.Contains("Customer (Added)", exception.Message);
        }

        Assert.False(await CustomerExistsAsync(smuggled.Id));
    }

    [Fact]
    public async Task The_context_the_app_uses_saves_the_requesting_tenants_own_row()
    {
        var world = await IsolationWorld.CreateAsync(factory);
        var own = Customer.Create(Guid.NewGuid(), world.Owner.TenantId, "Own", DateTimeOffset.UtcNow);

        using (var scope = ScopeFor(world.Owner.TenantId))
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ClearedDbContext>();
            dbContext.Customers.Add(own);
            await dbContext.SaveChangesAsync();
        }

        Assert.True(await CustomerExistsAsync(own.Id));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Saving_another_tenants_row_is_refused_for_every_filtered_type(string kind)
    {
        var row = Smuggled[kind](Guid.NewGuid());
        await using var dbContext = CreateContext(new FixedTenant(Guid.NewGuid()));
        dbContext.Add(row);

        var exception = await Assert.ThrowsAsync<TenantMismatchException>(() => dbContext.SaveChangesAsync());

        Assert.Equal(kind, row.GetType().Name);
        Assert.Contains($"{kind} (Added)", exception.Message);
    }

    [Fact]
    public async Task The_cases_above_cover_every_filtered_entity_type()
    {
        await using var dbContext = CreateContext(new FixedTenant(Guid.NewGuid()));

        var filtered = dbContext.Model.GetEntityTypes()
            .Where(entity => entity.FindProperty("TenantId") is not null && entity.GetDeclaredQueryFilters().Count > 0)
            .Select(entity => entity.ClrType.Name)
            .Order()
            .ToList();

        Assert.Equal(filtered, Smuggled.Keys.Order().ToList());
    }

    [Fact]
    public async Task Changing_another_tenants_customer_is_refused_and_changes_nothing()
    {
        var world = await IsolationWorld.CreateAsync(factory);
        var before = await world.SnapshotOwnerDataAsync();

        await using var dbContext = CreateContext(new FixedTenant(world.Intruder.TenantId));
        var customer = await dbContext.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == world.Customer.Id);
        customer.UpdateDetails("Hijacked");

        await Assert.ThrowsAsync<TenantMismatchException>(() => dbContext.SaveChangesAsync());

        Assert.Equal(before, await world.SnapshotOwnerDataAsync());
    }

    [Fact]
    public async Task Issuing_another_tenants_draft_invoice_is_refused_and_changes_nothing()
    {
        var world = await IsolationWorld.CreateAsync(factory);
        var before = await world.SnapshotOwnerDataAsync();

        await using var dbContext = CreateContext(new FixedTenant(world.Intruder.TenantId));
        var draft = await dbContext.Invoices.IgnoreQueryFilters().Include(i => i.Lines)
            .SingleAsync(i => i.Id == world.DraftInvoice.Id);
        draft.Issue("INV-9999", TestData.Today, TestData.Today, TestData.Today.AddDays(30), DocumentType.Invoice, 0.15m);

        await Assert.ThrowsAsync<TenantMismatchException>(() => dbContext.SaveChangesAsync());

        Assert.Equal(before, await world.SnapshotOwnerDataAsync());
    }

    [Fact]
    public async Task Deleting_another_tenants_customer_is_refused_and_deletes_nothing()
    {
        var world = await IsolationWorld.CreateAsync(factory);
        var before = await world.SnapshotOwnerDataAsync();

        await using var dbContext = CreateContext(new FixedTenant(world.Intruder.TenantId));
        var customer = await dbContext.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == world.Customer.Id);
        dbContext.Customers.Remove(customer);

        await Assert.ThrowsAsync<TenantMismatchException>(() => dbContext.SaveChangesAsync());

        Assert.Equal(before, await world.SnapshotOwnerDataAsync());
    }

    [Fact]
    public async Task Taking_over_another_tenants_row_by_rewriting_its_TenantId_is_refused()
    {
        var world = await IsolationWorld.CreateAsync(factory);
        var before = await world.SnapshotOwnerDataAsync();

        await using var dbContext = CreateContext(new FixedTenant(world.Intruder.TenantId));
        var customer = await dbContext.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == world.Customer.Id);
        dbContext.Entry(customer).Property(nameof(Customer.TenantId)).CurrentValue = world.Intruder.TenantId;

        await Assert.ThrowsAsync<TenantMismatchException>(() => dbContext.SaveChangesAsync());

        Assert.Equal(before, await world.SnapshotOwnerDataAsync());
    }

    [Fact]
    public async Task A_tenant_can_add_change_and_delete_its_own_rows()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateContext(new FixedTenant(tenantId));
        var customer = Customer.Create(Guid.NewGuid(), tenantId, "Own", DateTimeOffset.UtcNow);

        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync();

        customer.UpdateDetails("Own, renamed");
        await dbContext.SaveChangesAsync();

        dbContext.Customers.Remove(customer);
        await dbContext.SaveChangesAsync();

        Assert.False(await dbContext.Customers.AnyAsync(c => c.Id == customer.Id));
    }

    [Fact]
    public async Task Saving_tenant_owned_rows_with_no_tenant_in_scope_is_refused()
    {
        await using var dbContext = CreateContext(new NoTenant());
        dbContext.Customers.Add(Customer.Create(Guid.NewGuid(), Guid.NewGuid(), "Orphan", DateTimeOffset.UtcNow));

        var exception = await Assert.ThrowsAsync<TenantMismatchException>(() => dbContext.SaveChangesAsync());

        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task Rows_that_are_not_tenant_owned_never_ask_for_a_tenant()
    {
        await using var dbContext = CreateContext(new NoTenant());
        var tenant = Tenant.Register(
            Guid.NewGuid(), $"No Scope Co {Guid.NewGuid():N}", VatStatus.NotRegistered, null, DateTimeOffset.UtcNow);
        var email = $"user-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            TenantId = tenant.Id,
        };

        dbContext.Tenants.Add(tenant);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        Assert.True(await dbContext.Tenants.AnyAsync(t => t.Id == tenant.Id));
        Assert.True(await dbContext.Users.AnyAsync(u => u.Id == user.Id));
    }

    [Fact]
    public async Task The_synchronous_save_is_guarded_too()
    {
        await using var dbContext = CreateContext(new FixedTenant(Guid.NewGuid()));
        dbContext.Customers.Add(Customer.Create(Guid.NewGuid(), Guid.NewGuid(), "Smuggled", DateTimeOffset.UtcNow));

        Assert.Throws<TenantMismatchException>(() => dbContext.SaveChanges());
    }

    [Fact]
    public async Task A_mismatch_is_a_500_that_tells_the_client_nothing()
    {
        var handler = new DomainExceptionHandler(factory.Services.GetRequiredService<IHostEnvironment>());
        var httpContext = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var exception = new TenantMismatchException("Refusing to save Customer (Added): not owned by tenant X.");

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        httpContext.Response.Body.Position = 0;
        var body = await new StreamReader(httpContext.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("Customer", body);
    }

    private IServiceScope ScopeFor(Guid tenantId)
    {
        var scope = factory.Services.CreateScope();
        var identity = new ClaimsIdentity([new Claim("tenant_id", tenantId.ToString())], "Test");
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        return scope;
    }

    // Reads without the tenant filter, so it needs no tenant in scope.
    private async Task<bool> CustomerExistsAsync(Guid customerId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClearedDbContext>();

        return await dbContext.Customers.IgnoreQueryFilters().AnyAsync(c => c.Id == customerId);
    }

    private ClearedDbContext CreateContext(ITenantContext tenant)
    {
        // Must match Program.cs's AddDbContext configuration, snake_case naming included.
        var options = new DbContextOptionsBuilder<ClearedDbContext>()
            .UseNpgsql(factory.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantWriteInterceptor(tenant))
            .Options;

        return new ClearedDbContext(options, tenant);
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
    }

    // Behaves like HttpTenantContext on a request with no token.
    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("No tenant_id claim on the current request.");
    }
}
