using Cleared.Domain.Invoicing;

namespace Cleared.Application.Invoices;

// VatTreatment defaults to Standard when omitted: System.Text.Json leaves an absent enum
// at its C# default, value 0. InvoiceService overrides it to NotApplicable when the tenant
// is not VAT registered, since such a tenant cannot charge output VAT.
public sealed record CreateInvoiceLineRequest(
    string Description, decimal Quantity, string UnitPrice, VatTreatment VatTreatment = VatTreatment.Standard);

public sealed record CreateInvoiceRequest(Guid CustomerId, IReadOnlyList<CreateInvoiceLineRequest> Lines);
