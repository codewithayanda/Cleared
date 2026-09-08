namespace Cleared.Domain.Invoicing;

// Determined once, at Issue(), from the tenant's VatStatus at that moment (see
// Tenant.CanIssueTaxInvoices), then frozen onto the invoice forever. A tenant that later
// registers for VAT must not cause its past Invoice-type documents to retroactively
// become tax invoices.
public enum DocumentType
{
    Invoice,
    TaxInvoice,
}
