using Cleared.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Cleared.Integration.Tests;

// The port on its own, against the real database. InvoiceConcurrencyTests covers it end to end.
[Collection(ApiCollection.Name)]
public class InvoiceLockTests(ClearedApiFactory factory)
{
    // A lock taken outside a transaction is released at once, so the lock refuses to pretend.
    [Fact]
    public async Task Locking_outside_a_transaction_is_refused()
    {
        using var scope = factory.Services.CreateScope();
        var invoiceLock = scope.ServiceProvider.GetRequiredService<IInvoiceLock>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => invoiceLock.AcquireAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    // The SQL is hand-written, so EF's tenant filter does not protect it. tenant_id has to.
    [Fact]
    public async Task Another_tenant_cannot_lock_an_invoice_it_does_not_own()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Lock Customer");
        var invoice = await owner.Client.CreateIssuedInvoiceAsync(customer.Id);

        using var scope = factory.Services.CreateScope();
        await using var transaction = await scope.ServiceProvider
            .GetRequiredService<IUnitOfWork>().BeginTransactionAsync(CancellationToken.None);
        var invoiceLock = scope.ServiceProvider.GetRequiredService<IInvoiceLock>();

        Assert.False(await invoiceLock.AcquireAsync(Guid.NewGuid(), invoice.Id, CancellationToken.None));
        Assert.True(await invoiceLock.AcquireAsync(owner.TenantId, invoice.Id, CancellationToken.None));
    }

    [Fact]
    public async Task A_second_holder_waits_until_the_first_transaction_ends()
    {
        var owner = await factory.CreateTenantAsync();
        var customer = await owner.Client.CreateCustomerAsync("Lock Customer");
        var invoice = await owner.Client.CreateIssuedInvoiceAsync(customer.Id);

        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var firstTransaction = await firstScope.ServiceProvider
            .GetRequiredService<IUnitOfWork>().BeginTransactionAsync(CancellationToken.None);
        await using var secondTransaction = await secondScope.ServiceProvider
            .GetRequiredService<IUnitOfWork>().BeginTransactionAsync(CancellationToken.None);

        Assert.True(await firstScope.ServiceProvider.GetRequiredService<IInvoiceLock>()
            .AcquireAsync(owner.TenantId, invoice.Id, CancellationToken.None));

        var second = secondScope.ServiceProvider.GetRequiredService<IInvoiceLock>()
            .AcquireAsync(owner.TenantId, invoice.Id, CancellationToken.None);

        // Still queued behind the first holder.
        Assert.NotSame(second, await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(500))));

        // Ending the first transaction without a commit releases the lock.
        await firstTransaction.DisposeAsync();

        Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
