using Cleared.Application.Abstractions;
using Cleared.Application.Customers;
using Cleared.Application.Invoices;
using Cleared.Application.Tenants;
using Cleared.Domain.Auditing;
using Cleared.Domain.Invoicing;
using Cleared.Domain.Payments;
using Cleared.Domain.Tenancy;

namespace Cleared.Application.Tests.TestDoubles;

// Hand-written fakes rather than a mocking library — these services have few enough
// dependencies that a plain in-memory implementation is simpler to read than a
// framework's setup/verify ceremony, and adds no new package to a company machine where
// every dependency is deliberately kept to what's actually needed.
internal sealed class FakeTenantRepository : ITenantRepository
{
    private readonly Dictionary<Guid, Tenant> _tenants = [];

    public void Seed(Tenant tenant) => _tenants[tenant.Id] = tenant;

    public Task AddAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        _tenants[tenant.Id] = tenant;
        return Task.CompletedTask;
    }

    public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(_tenants.GetValueOrDefault(tenantId));
}

internal sealed class FakeCustomerRepository : ICustomerRepository
{
    private readonly Dictionary<Guid, Customer> _customers = [];

    public void Seed(Customer customer) => _customers[customer.Id] = customer;

    public Task AddAsync(Customer customer, CancellationToken cancellationToken)
    {
        _customers[customer.Id] = customer;
        return Task.CompletedTask;
    }

    public Task<Customer?> GetByIdAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(_customers.GetValueOrDefault(customerId) is { } c && c.TenantId == tenantId ? c : null);

    public Task<IReadOnlyList<Customer>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Customer>>(_customers.Values.Where(c => c.TenantId == tenantId).ToList());
}

internal sealed class FakeInvoiceRepository : IInvoiceRepository
{
    private readonly Dictionary<Guid, Invoice> _invoices = [];

    public void Seed(Invoice invoice) => _invoices[invoice.Id] = invoice;

    public Task AddAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        _invoices[invoice.Id] = invoice;
        return Task.CompletedTask;
    }

    public Task<Invoice?> GetByIdAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken) =>
        Task.FromResult(_invoices.GetValueOrDefault(invoiceId) is { } i && i.TenantId == tenantId ? i : null);

    public Task<IReadOnlyList<Invoice>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Invoice>>(_invoices.Values.Where(i => i.TenantId == tenantId).ToList());
}

internal sealed class FakeCreditNoteRepository : ICreditNoteRepository
{
    private readonly List<CreditNote> _creditNotes = [];

    public Task AddAsync(CreditNote creditNote, CancellationToken cancellationToken)
    {
        _creditNotes.Add(creditNote);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CreditNote>> ListByInvoiceIdAsync(
        Guid tenantId, Guid invoiceId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CreditNote>>(
            _creditNotes.Where(cn => cn.TenantId == tenantId && cn.InvoiceId == invoiceId).ToList());
}

internal sealed class FakeVatRateRepository(decimal rate = 0.15m) : IVatRateRepository
{
    public Task<VatRate?> GetEffectiveRateAsync(DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<VatRate?>(VatRate.Create(Guid.NewGuid(), rate, new DateOnly(2018, 4, 1)));
}

internal sealed class FakeInvoiceNumberAllocator : IInvoiceNumberAllocator
{
    private int _next = 1;

    public Task<string> AllocateAsync(Guid tenantId, int year, CancellationToken cancellationToken) =>
        Task.FromResult($"INV-{year}-{_next++:D4}");
}

internal sealed class FakeCreditNoteNumberAllocator : ICreditNoteNumberAllocator
{
    private int _next = 1;

    public Task<string> AllocateAsync(Guid tenantId, int year, CancellationToken cancellationToken) =>
        Task.FromResult($"CN-{year}-{_next++:D4}");
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Task.FromResult<ITransaction>(new NoOpTransaction());

    private sealed class NoOpTransaction : ITransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class FakeClock(DateOnly today) : IClock
{
    public DateTimeOffset UtcNow => today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public DateOnly Today => today;
}

internal sealed class FakePaymentRepository : IPaymentRepository
{
    private readonly List<Payment> _payments = [];

    public void Seed(Payment payment) => _payments.Add(payment);

    public Task AddAsync(Payment payment, CancellationToken cancellationToken)
    {
        _payments.Add(payment);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Payment>> ListByInvoiceIdAsync(
        Guid tenantId, Guid invoiceId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Payment>>(
            _payments.Where(p => p.TenantId == tenantId && p.InvoiceId == invoiceId).ToList());

    public Task<IReadOnlyList<Payment>> ListByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Payment>>(_payments.Where(p => p.TenantId == tenantId).ToList());
}

internal sealed class FakeAuditLogRepository : IAuditLogRepository
{
    public List<AuditLog> Entries { get; } = [];

    public Task AddAsync(AuditLog entry, CancellationToken cancellationToken)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditLog>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditLog>>(Entries.Where(e => e.TenantId == tenantId).ToList());
}

internal sealed class FakeCurrentUserContext(Guid userId) : ICurrentUserContext
{
    public Guid UserId => userId;
}

internal sealed class FakeInvoicePdfRenderer : IInvoicePdfRenderer
{
    public byte[] Render(InvoiceResponse invoice, CustomerResponse customer, TenantResponse tenant) => [1, 2, 3];
}
