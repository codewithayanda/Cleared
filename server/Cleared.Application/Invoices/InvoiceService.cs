using System.Globalization;
using Cleared.Application.Abstractions;
using Cleared.Domain.Common;
using Cleared.Domain.Invoicing;

namespace Cleared.Application.Invoices;

public sealed class InvoiceService(
    IInvoiceRepository invoiceRepository, IInvoiceNumberAllocator numberAllocator, IUnitOfWork unitOfWork)
{
    public async Task<InvoiceResponse> CreateAsync(
        Guid tenantId, CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = Invoice.CreateDraft(Guid.NewGuid(), tenantId, request.CustomerId);

        foreach (var line in request.Lines)
        {
            var unitPrice = Money.Zar(decimal.Parse(line.UnitPrice, CultureInfo.InvariantCulture));
            invoice.AddLine(Guid.NewGuid(), line.Description, line.Quantity, unitPrice);
        }

        await invoiceRepository.AddAsync(invoice, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return InvoiceMapper.ToResponse(invoice);
    }

    public async Task<InvoiceResponse?> GetByIdAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await invoiceRepository.GetByIdAsync(tenantId, invoiceId, cancellationToken);

        return invoice is null ? null : InvoiceMapper.ToResponse(invoice);
    }

    public async Task<IReadOnlyList<InvoiceResponse>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var invoices = await invoiceRepository.ListAsync(tenantId, cancellationToken);

        return invoices.Select(InvoiceMapper.ToResponse).ToList();
    }

    public async Task<InvoiceResponse?> IssueAsync(
        Guid tenantId, Guid invoiceId, IssueInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await invoiceRepository.GetByIdAsync(tenantId, invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        // Claiming the number and saving the issued invoice must succeed or fail together —
        // otherwise a save failure after the number is claimed leaves a permanent gap.
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var number = await numberAllocator.AllocateAsync(tenantId, request.IssueDate.Year, cancellationToken);
        invoice.Issue(number, request.IssueDate, request.DueDate);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return InvoiceMapper.ToResponse(invoice);
    }
}
