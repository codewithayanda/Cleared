namespace Cleared.Application.Invoices;

// SupplyDate defaults to IssueDate when omitted, since the common case is supplied and
// invoiced the same day and the client doesn't collect a separate one. It only needs to
// differ for retrospective invoicing across a VAT rate change.
public sealed record IssueInvoiceRequest(DateOnly IssueDate, DateOnly DueDate, DateOnly? SupplyDate = null);
