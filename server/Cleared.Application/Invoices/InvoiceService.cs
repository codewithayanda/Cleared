using System.Globalization;
using Cleared.Application.Abstractions;
using Cleared.Application.Customers;
using Cleared.Application.Tenants;
using Cleared.Domain.Auditing;
using Cleared.Domain.Common;
using Cleared.Domain.Invoicing;
using Cleared.Domain.Tenancy;

namespace Cleared.Application.Invoices;

public sealed class InvoiceService(
    IInvoiceRepository invoiceRepository,
    ITenantRepository tenantRepository,
    ICustomerRepository customerRepository,
    IVatRateRepository vatRateRepository,
    IInvoiceNumberAllocator numberAllocator,
    IAuditLogRepository auditLogRepository,
    IPaymentRepository paymentRepository,
    IInvoicePdfRenderer pdfRenderer,
    IUnitOfWork unitOfWork,
    ICurrentUserContext currentUserContext,
    IClock clock)
{
    public async Task<InvoiceResponse> CreateAsync(
        Guid tenantId, CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var tenant = await tenantRepository.GetByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException("The current tenant no longer exists.");

        var invoice = Invoice.CreateDraft(Guid.NewGuid(), tenantId, request.CustomerId);

        foreach (var line in request.Lines)
        {
            var unitPrice = Money.Zar(decimal.Parse(line.UnitPrice, CultureInfo.InvariantCulture));

            // A tenant that isn't VAT registered cannot charge output VAT on anything, by
            // law — that overrides whatever treatment the client requested.
            var vatTreatment = tenant.VatStatus == VatStatus.NotRegistered
                ? VatTreatment.NotApplicable
                : line.VatTreatment;

            invoice.AddLine(Guid.NewGuid(), line.Description, line.Quantity, unitPrice, vatTreatment);
        }

        await invoiceRepository.AddAsync(invoice, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // A brand-new draft can't have a payment against it yet.
        return InvoiceMapper.ToResponse(invoice, Money.Zero);
    }

    public async Task<InvoiceResponse?> GetByIdAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await invoiceRepository.GetByIdAsync(tenantId, invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var amountPaid = await GetAmountPaidAsync(tenantId, invoiceId, cancellationToken);

        return InvoiceMapper.ToResponse(invoice, amountPaid);
    }

    public async Task<IReadOnlyList<InvoiceResponse>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var invoices = await invoiceRepository.ListAsync(tenantId, cancellationToken);

        // One query for every payment the tenant has ever made, rather than one query per
        // invoice — the latter is exactly the N+1 this app's own bottleneck curriculum
        // warns against, and would scale with invoice count on the app's busiest screen.
        var payments = await paymentRepository.ListByTenantIdAsync(tenantId, cancellationToken);
        var amountPaidByInvoiceId = payments
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => g.Aggregate(Money.Zero, (sum, p) => sum.Add(p.Amount)));

        return invoices
            .Select(invoice => InvoiceMapper.ToResponse(
                invoice, amountPaidByInvoiceId.GetValueOrDefault(invoice.Id, Money.Zero)))
            .ToList();
    }

    public async Task<InvoiceResponse?> IssueAsync(
        Guid tenantId, Guid invoiceId, IssueInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await invoiceRepository.GetByIdAsync(tenantId, invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var tenant = await tenantRepository.GetByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException("The current tenant no longer exists.");

        var supplyDate = request.SupplyDate ?? request.IssueDate;

        var vatRate = await vatRateRepository.GetEffectiveRateAsync(supplyDate, cancellationToken)
            ?? throw new InvalidOperationException($"No VAT rate is configured for {supplyDate:yyyy-MM-dd}.");

        var documentType = tenant.CanIssueTaxInvoices() ? DocumentType.TaxInvoice : DocumentType.Invoice;

        if (documentType == DocumentType.TaxInvoice)
        {
            var customer = await customerRepository.GetByIdAsync(tenantId, invoice.CustomerId, cancellationToken)
                ?? throw new InvalidOperationException("The customer on this invoice no longer exists.");

            // Must run — and can fail — before Issue() below mutates anything, so a
            // rejected tax invoice leaves the draft untouched and nothing is persisted.
            ValidateTaxInvoiceFields(tenant, customer, invoice.ProspectiveTotal(vatRate.Rate));
        }

        // Claiming the number and saving the issued invoice must succeed or fail together —
        // otherwise a save failure after the number is claimed leaves a permanent gap.
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var number = await numberAllocator.AllocateAsync(tenantId, request.IssueDate.Year, cancellationToken);
        invoice.Issue(number, request.IssueDate, supplyDate, request.DueDate, documentType, vatRate.Rate);

        var auditEntry = AuditLog.Record(
            Guid.NewGuid(), tenantId, currentUserContext.UserId, "Invoice", invoice.Id, "Issued", clock.UtcNow,
            $"Issued as {number}, total R{invoice.Total.Amount:F2}.");
        await auditLogRepository.AddAsync(auditEntry, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Issuing is the transition out of Draft — nothing could have paid against this
        // invoice before this call, since a payment requires an already-issued invoice.
        return InvoiceMapper.ToResponse(invoice, Money.Zero);
    }

    public async Task<byte[]?> GetPdfAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await invoiceRepository.GetByIdAsync(tenantId, invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var tenant = await tenantRepository.GetByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException("The current tenant no longer exists.");

        var customer = await customerRepository.GetByIdAsync(tenantId, invoice.CustomerId, cancellationToken)
            ?? throw new InvalidOperationException("The customer on this invoice no longer exists.");

        var amountPaid = await GetAmountPaidAsync(tenantId, invoiceId, cancellationToken);

        return pdfRenderer.Render(
            InvoiceMapper.ToResponse(invoice, amountPaid), CustomerMapper.ToResponse(customer),
            TenantMapper.ToResponse(tenant));
    }

    private async Task<Money> GetAmountPaidAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var payments = await paymentRepository.ListByInvoiceIdAsync(tenantId, invoiceId, cancellationToken);

        return payments.Aggregate(Money.Zero, (sum, payment) => sum.Add(payment.Amount));
    }

    // VAT Act s20(4): every tax invoice must identify BOTH parties — supplier and
    // recipient — by name and address; at or above R5,000 it must also carry the
    // recipient's own VAT number (an "abridged" tax invoice below that threshold may
    // omit it). The supplier's own VAT number is already guaranteed by
    // Tenant.Register/RegisterForVat, which refuse a Registered tenant with no VAT
    // number — there's nothing left to check for that one here.
    private static void ValidateTaxInvoiceFields(Tenant tenant, Customer customer, Money prospectiveTotal)
    {
        var missingFields = new List<string>();

        if (string.IsNullOrWhiteSpace(tenant.Address))
        {
            missingFields.Add("your company address (in company settings)");
        }

        if (string.IsNullOrWhiteSpace(customer.Name))
        {
            missingFields.Add("customer name");
        }

        if (string.IsNullOrWhiteSpace(customer.Address))
        {
            missingFields.Add("customer address");
        }

        if (prospectiveTotal.Amount >= 5000m && string.IsNullOrWhiteSpace(customer.VatNumber))
        {
            missingFields.Add("customer VAT number (required for tax invoices of R5,000 or more)");
        }

        if (missingFields.Count > 0)
        {
            throw new TaxInvoiceValidationException(missingFields);
        }
    }
}
