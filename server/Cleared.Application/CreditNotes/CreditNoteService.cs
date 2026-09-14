using System.Globalization;
using Cleared.Application.Abstractions;
using Cleared.Domain.Auditing;
using Cleared.Domain.Common;
using Cleared.Domain.Invoicing;

namespace Cleared.Application.CreditNotes;

public sealed class CreditNoteService(
    IInvoiceRepository invoiceRepository,
    ICreditNoteRepository creditNoteRepository,
    ICreditNoteNumberAllocator numberAllocator,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUserContext,
    IClock clock)
{
    // Returns null for an invoice this tenant cannot see, including one belonging to another
    // tenant, so the controller maps both to a plain 404. Never 403: a separate status for
    // "exists but isn't yours" would confirm the id refers to something real.
    public async Task<CreditNoteResponse?> CreateAsync(
        Guid tenantId, Guid invoiceId, CreateCreditNoteRequest request, CancellationToken cancellationToken)
    {
        var invoice = await invoiceRepository.GetByIdAsync(tenantId, invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        if (invoice.Status is not (InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid or InvoiceStatus.Paid))
        {
            throw new InvalidOperationException(
                "A credit note can only be issued against an issued, partially paid or paid invoice.");
        }

        if (request.Lines.Count == 0)
        {
            throw new ArgumentException("A credit note must have at least one line.", nameof(request));
        }

        var existingCreditNotes =
            await creditNoteRepository.ListByInvoiceIdAsync(tenantId, invoiceId, cancellationToken);

        // How much of each original line a prior credit note already covered, so a second
        // note against the same invoice cannot credit more than what's left.
        // TODO: this read-then-write is not serialised. READ COMMITTED lets two concurrent
        // notes each credit the full remainder. Needs SELECT ... FOR UPDATE on the invoice.
        var alreadyCredited = existingCreditNotes
            .SelectMany(creditNote => creditNote.Lines)
            .GroupBy(line => line.InvoiceLineItemId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));

        var lineRequests = new List<CreditNoteLineRequest>();

        foreach (var lineRequest in request.Lines)
        {
            var originalLine = invoice.Lines.FirstOrDefault(line => line.Id == lineRequest.InvoiceLineItemId)
                ?? throw new ArgumentException(
                    $"Line '{lineRequest.InvoiceLineItemId}' does not belong to this invoice.", nameof(request));

            var remainingQuantity = originalLine.Quantity - alreadyCredited.GetValueOrDefault(originalLine.Id);

            if (lineRequest.Quantity <= 0 || lineRequest.Quantity > remainingQuantity)
            {
                throw new ArgumentException(
                    $"Cannot credit {lineRequest.Quantity} of '{originalLine.Description}': " +
                    $"only {remainingQuantity} remains creditable.",
                    nameof(request));
            }

            lineRequests.Add(new CreditNoteLineRequest(
                originalLine.Id, originalLine.Description, lineRequest.Quantity,
                originalLine.UnitPrice, originalLine.VatTreatment));
        }

        // Claiming the number and saving the credit note must succeed or fail together.
        // See InvoiceNumberAllocator.
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var number = await numberAllocator.AllocateAsync(tenantId, clock.Today.Year, cancellationToken);

        var creditNote = CreditNote.Create(
            Guid.NewGuid(), tenantId, invoiceId, number, request.Reason, clock.Today,
            invoice.VatRateApplied!.Value, lineRequests);

        await creditNoteRepository.AddAsync(creditNote, cancellationToken);

        // Fully credited means every line's cumulative credited quantity, across this note
        // and all prior ones, now reaches what was invoiced, so nothing is left owing and
        // the invoice is cancelled. A partial credit leaves the document in place.
        var fullyCredited = invoice.Lines.All(line =>
        {
            var creditedByThisNote = lineRequests
                .Where(l => l.InvoiceLineItemId == line.Id)
                .Sum(l => l.Quantity);

            return alreadyCredited.GetValueOrDefault(line.Id) + creditedByThisNote >= line.Quantity;
        });

        if (fullyCredited)
        {
            invoice.Cancel();
        }

        var auditEntry = AuditLog.Record(
            Guid.NewGuid(), tenantId, currentUserContext.UserId, "Invoice", invoiceId, "CreditNoteIssued",
            clock.UtcNow,
            $"Issued credit note {number} for R{creditNote.Total.Amount:F2}. Reason: {request.Reason}" +
            (fullyCredited ? " (invoice fully credited, now cancelled)." : "."));
        await auditLogRepository.AddAsync(auditEntry, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ToResponse(creditNote);
    }

    private static CreditNoteResponse ToResponse(CreditNote creditNote) => new(
        creditNote.Id,
        creditNote.TenantId,
        creditNote.InvoiceId,
        creditNote.Number,
        creditNote.Reason,
        creditNote.IssueDate,
        FormatMoney(creditNote.Subtotal),
        FormatMoney(creditNote.VatTotal),
        FormatMoney(creditNote.Total),
        creditNote.Lines
            .Select(l => new CreditNoteLineResponse(
                l.Id,
                l.InvoiceLineItemId,
                l.Description,
                l.Quantity,
                FormatMoney(l.UnitPrice),
                FormatMoney(l.LineSubtotal),
                l.VatTreatment.ToString(),
                FormatMoney(l.LineVat),
                FormatMoney(l.LineTotal)))
            .ToList());

    private static string FormatMoney(Money money) => money.Amount.ToString("F2", CultureInfo.InvariantCulture);
}
