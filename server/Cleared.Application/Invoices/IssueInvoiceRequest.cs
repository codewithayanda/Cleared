namespace Cleared.Application.Invoices;

// SupplyDate defaults to IssueDate when omitted — the common case is "supplied and
// invoiced the same day", and the existing client doesn't collect a separate one. It only
// needs to differ for retrospective invoicing across a VAT rate change boundary.
public sealed record IssueInvoiceRequest(DateOnly IssueDate, DateOnly DueDate, DateOnly? SupplyDate = null);
