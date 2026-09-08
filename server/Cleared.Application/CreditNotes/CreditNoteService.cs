using System.Globalization;
using Cleared.Application.Abstractions;
using Cleared.Domain.Common;
using Cleared.Domain.Invoicing;

namespace Cleared.Application.CreditNotes;

public sealed class CreditNoteService(
    IInvoiceRepository invoiceRepository,
    ICreditNoteRepository creditNoteRepository,
    ICreditNoteNumberAllocator numberAllocator,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    // Returns null for an invoice that doesn't exist for this tenant — including one that
    // belongs to a different tenant entirely — so the controller can map that to a plain
    // 404, indistinguishable from genuine absence. Never 403: a different status for
    // "exists but isn't yours" would confirm the id refers to something real.
    public async Task<CreditNoteResponse?> CreateAsync(
        Guid tenantId, Guid invoiceId, CreateCreditNoteRequest request, CancellationToken cancellationToken)
    {
        var invoice = await invoiceRepository.GetByIdAsync(tenantId, invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        if (invoice.Status != InvoiceStatus.Issued)
        {
            throw new InvalidOperationException("A credit note can only be issued against an issued invoice.");
        }

        if (request.Lines.Count == 0)
        {
            throw new ArgumentException("A credit note must have at least one line.", nameof(request));
        }

        var existingCreditNotes =
            await creditNoteRepository.ListByInvoiceIdAsync(tenantId, invoiceId, cancellationToken);

        // How much of each original line has already been credited by a prior credit
        // note — a second credit note against the same invoice must not be able to credit
        // more than what's left.
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
                    $"Cannot credit {lineRequest.Quantity} of '{originalLine.Description}' — " +
                    $"only {remainingQuantity} remains creditable.",
                    nameof(request));
            }

            lineRequests.Add(new CreditNoteLineRequest(
                originalLine.Id, originalLine.Description, lineRequest.Quantity,
                originalLine.UnitPrice, originalLine.VatTreatment));
        }

        // Claiming the number and saving the credit note must succeed or fail together —
        // same reasoning as invoice numbering (see InvoiceNumberAllocator).
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var number = await numberAllocator.AllocateAsync(tenantId, clock.Today.Year, cancellationToken);

        var creditNote = CreditNote.Create(
            Guid.NewGuid(), tenantId, invoiceId, number, request.Reason, clock.Today,
            invoice.VatRateApplied!.Value, lineRequests);

        await creditNoteRepository.AddAsync(creditNote, cancellationToken);

        // Fully credited — every line's cumulative credited quantity, across this credit
        // note and all prior ones, now reaches what was originally invoiced — means
        // nothing is left owing, so the invoice is cancelled. A partial credit reduces
        // what's owed without retiring the document itself.
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
