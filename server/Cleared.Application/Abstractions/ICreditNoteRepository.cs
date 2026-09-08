using Cleared.Domain.Invoicing;

namespace Cleared.Application.Abstractions;

public interface ICreditNoteRepository
{
    Task AddAsync(CreditNote creditNote, CancellationToken cancellationToken);

    Task<IReadOnlyList<CreditNote>> ListByInvoiceIdAsync(
        Guid tenantId, Guid invoiceId, CancellationToken cancellationToken);
}
