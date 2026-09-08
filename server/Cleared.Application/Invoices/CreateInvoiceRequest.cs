using Cleared.Domain.Invoicing;

namespace Cleared.Application.Invoices;

// VatTreatment defaults to Standard when omitted (System.Text.Json leaves an absent enum
// field at its C# default, which is VatTreatment.Standard — value 0) — the safe default
// for the common case of ordinary taxable goods/services. InvoiceService overrides it to
// NotApplicable regardless of what's requested when the tenant isn't VAT registered, since
// such a tenant cannot charge output VAT on anything, by law.
public sealed record CreateInvoiceLineRequest(
    string Description, decimal Quantity, string UnitPrice, VatTreatment VatTreatment = VatTreatment.Standard);

public sealed record CreateInvoiceRequest(Guid CustomerId, IReadOnlyList<CreateInvoiceLineRequest> Lines);
