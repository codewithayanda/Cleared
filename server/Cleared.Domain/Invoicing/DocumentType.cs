namespace Cleared.Domain.Invoicing;

// Determined once, at Issue(), from the tenant's VatStatus at that moment (see
// Tenant.CanIssueTaxInvoices), then frozen. A tenant that later registers for VAT must not
// turn its past Invoice-type documents into tax invoices retroactively.
public enum DocumentType
{
    Invoice,
    TaxInvoice,
}
