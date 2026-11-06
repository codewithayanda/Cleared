using Cleared.Application.Abstractions;
using Cleared.Application.Common;
using Cleared.Application.Customers;
using Cleared.Application.Payments;
using Cleared.Application.Tenants;
using Cleared.Domain.Auditing;
using Cleared.Domain.Common;
using Cleared.Domain.Invoicing;
using Cleared.Domain.Payments;
using Cleared.Domain.Tenancy;

namespace Cleared.Application.Invoices;

public sealed class InvoiceService(
    IInvoiceRepository invoiceRepository,
    IInvoiceLock invoiceLock,
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

        // The id comes from the request body, so resolve it under this tenant. Another
        // tenant's customer and a missing one get the same answer.
        if (await customerRepository.GetByIdAsync(tenantId, request.CustomerId, cancellationToken) is null)
        {
            throw new ArgumentException("The customer does not exist.", nameof(request));
        }

        var invoice = Invoice.CreateDraft(Guid.NewGuid(), tenantId, request.CustomerId, clock.UtcNow);

        foreach (var line in request.Lines.Select(line => ToDraftLine(tenant, line)))
        {
            invoice.AddLine(line.Id, line.Description, line.Quantity, line.UnitPrice, line.VatTreatment);
        }

        await invoiceRepository.AddAsync(invoice, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // A brand-new draft can't have a payment against it yet.
        return InvoiceMapper.ToResponse(invoice, Money.Zero, clock.Today);
    }

    public async Task<InvoiceResponse?> GetByIdAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await invoiceRepository.GetByIdAsync(tenantId, invoiceId, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var amountPaid = await GetAmountPaidAsync(tenantId, invoiceId, cancellationToken);

        return InvoiceMapper.ToResponse(invoice, amountPaid, clock.Today);
    }

    public async Task<IReadOnlyList<InvoiceResponse>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var invoices = await invoiceRepository.ListAsync(tenantId, cancellationToken);

        // One query for the tenant's payments rather than one per invoice, which would be
        // an N+1 on the busiest screen in the app.
        // TODO: unbounded. Neither this nor the invoice list is paginated, so both grow
        // with tenant age. Move the sum into a GROUP BY over one page of invoice ids.
        var payments = await paymentRepository.ListByTenantIdAsync(tenantId, cancellationToken);
        var amountPaidByInvoiceId = payments
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => g.Aggregate(Money.Zero, (sum, p) => sum.Add(p.Amount)));

        var today = clock.Today;

        return invoices
            .Select(invoice => InvoiceMapper.ToResponse(
                invoice, amountPaidByInvoiceId.GetValueOrDefault(invoice.Id, Money.Zero), today))
            .ToList();
    }

    public async Task<InvoiceResponse?> UpdateAsync(
        Guid tenantId, Guid invoiceId, UpdateInvoiceRequest request, CancellationToken cancellationToken)
    {
        // The row lock makes a change wait for an issue that is under way and then find the invoice issued,
        // instead of rewriting lines under a total that has already been fixed.
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var invoice = await invoiceLock.AcquireAsync(tenantId, invoiceId, cancellationToken)
            ? await invoiceRepository.GetByIdAsync(tenantId, invoiceId, cancellationToken)
            : null;
        if (invoice is null)
        {
            return null;
        }

        var tenant = await tenantRepository.GetByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException("The current tenant no longer exists.");

        // Another tenant's customer and a missing one get the same answer, as when creating.
        if (await customerRepository.GetByIdAsync(tenantId, request.CustomerId, cancellationToken) is null)
        {
            throw new ArgumentException("The customer does not exist.", nameof(request));
        }

        invoice.Revise(request.CustomerId, [.. request.Lines.Select(line => ToDraftLine(tenant, line))]);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Only a draft can be changed, and a draft has no payments.
        return InvoiceMapper.ToResponse(invoice, Money.Zero, clock.Today);
    }

    public async Task<bool> DeleteAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var invoice = await invoiceLock.AcquireAsync(tenantId, invoiceId, cancellationToken)
            ? await invoiceRepository.GetByIdAsync(tenantId, invoiceId, cancellationToken)
            : null;
        if (invoice is null)
        {
            return false;
        }

        invoice.EnsureDeletable();

        invoiceRepository.Remove(invoice);
        var auditEntry = AuditLog.Record(
            Guid.NewGuid(), tenantId, currentUserContext.UserId, "Invoice", invoice.Id, "Deleted", clock.UtcNow,
            $"Deleted a draft with {invoice.Lines.Count} line(s), subtotal R{invoice.Subtotal.Amount:F2}.");
        await auditLogRepository.AddAsync(auditEntry, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<InvoiceResponse?> IssueAsync(
        Guid tenantId, Guid invoiceId, IssueInvoiceRequest request, CancellationToken cancellationToken)
    {
        // Claiming the number and saving the invoice must succeed or fail together, or a save
        // failure after the claim leaves a permanent gap in the sequence. The invoice row lock
        // also stops two callers issuing one draft, which would burn a number and overwrite
        // the first. See InvoiceConcurrencyTests.
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var invoice = await invoiceLock.AcquireAsync(tenantId, invoiceId, cancellationToken)
            ? await invoiceRepository.GetByIdAsync(tenantId, invoiceId, cancellationToken)
            : null;
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

        var customer = await customerRepository.GetByIdAsync(tenantId, invoice.CustomerId, cancellationToken)
            ?? throw new InvalidOperationException("The customer on this invoice no longer exists.");

        if (documentType == DocumentType.TaxInvoice)
        {
            // Runs before Issue() mutates anything, so a rejected tax invoice leaves the
            // draft untouched and nothing is persisted.
            ValidateTaxInvoiceFields(tenant, customer, invoice.ProspectiveTotal(vatRate.Rate));
        }

        var number = await numberAllocator.AllocateAsync(tenantId, request.IssueDate.Year, cancellationToken);
        invoice.Issue(
            number, request.IssueDate, supplyDate, request.DueDate, documentType, vatRate.Rate,
            new CustomerDetails(customer.Name, customer.Address, customer.VatNumber),
            new SupplierDetails(tenant.CompanyName, tenant.TradingName, tenant.Address, tenant.VatNumber));

        var auditEntry = AuditLog.Record(
            Guid.NewGuid(), tenantId, currentUserContext.UserId, "Invoice", invoice.Id, "Issued", clock.UtcNow,
            $"Issued as {number}, total R{invoice.Total.Amount:F2}.");
        await auditLogRepository.AddAsync(auditEntry, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Issuing is the transition out of Draft, and a payment requires an issued invoice,
        // so nothing can have paid against this one yet.
        return InvoiceMapper.ToResponse(invoice, Money.Zero, clock.Today);
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

        var customer = await CustomerForDocumentAsync(invoice, cancellationToken);
        var payments = await paymentRepository.ListByInvoiceIdAsync(tenantId, invoiceId, cancellationToken);
        var today = clock.Today;

        return pdfRenderer.Render(InvoiceDocumentBuilder.Build(
            InvoiceMapper.ToResponse(invoice, SumOf(payments), today),
            customer,
            SupplierForDocument(invoice, tenant),
            [.. payments.Select(PaymentMapper.ToResponse)],
            today));
    }

    // An issued invoice shows the details it was issued with, so a later edit cannot rewrite it. A draft
    // has none yet and shows what the customer has now.
    private async Task<CustomerResponse> CustomerForDocumentAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        if (invoice.IssuedTo is { } issuedTo)
        {
            return new CustomerResponse(
                invoice.CustomerId, invoice.TenantId, issuedTo.Name, issuedTo.VatNumber, null, issuedTo.Address);
        }

        var customer = await customerRepository.GetByIdAsync(invoice.TenantId, invoice.CustomerId, cancellationToken)
            ?? throw new InvalidOperationException("The customer on this invoice no longer exists.");

        return CustomerMapper.ToResponse(customer);
    }

    // The bank details stay live on purpose, so an invoice that is still owed shows where to pay now.
    private static TenantResponse SupplierForDocument(Invoice invoice, Tenant tenant) =>
        invoice.IssuedBy is { } issuedBy
            ? new TenantResponse(
                tenant.Id, issuedBy.CompanyName, issuedBy.TradingName, tenant.VatStatus.ToString(),
                issuedBy.VatNumber, issuedBy.Address, tenant.BankName, tenant.BankAccountNumber,
                tenant.BankBranchCode)
            : TenantMapper.ToResponse(tenant);

    // A tenant that is not VAT registered cannot charge output VAT, which overrides whatever treatment the
    // client requested.
    private static DraftLine ToDraftLine(Tenant tenant, CreateInvoiceLineRequest line) => new(
        Guid.NewGuid(),
        line.Description,
        line.Quantity,
        Money.Zar(MoneyText.Parse(line.UnitPrice, "Unit price")),
        tenant.VatStatus == VatStatus.NotRegistered ? VatTreatment.NotApplicable : line.VatTreatment);

    private async Task<Money> GetAmountPaidAsync(Guid tenantId, Guid invoiceId, CancellationToken cancellationToken) =>
        SumOf(await paymentRepository.ListByInvoiceIdAsync(tenantId, invoiceId, cancellationToken));

    private static Money SumOf(IEnumerable<Payment> payments) =>
        payments.Aggregate(Money.Zero, (sum, payment) => sum.Add(payment.Amount));

    // VAT Act s20(4): a tax invoice must identify both supplier and recipient by name and
    // address, and at or above R5,000 must carry the recipient's VAT number. The supplier's
    // own VAT number is already guaranteed by Tenant.Register and RegisterForVat.
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
