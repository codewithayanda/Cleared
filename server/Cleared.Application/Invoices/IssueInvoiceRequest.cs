namespace Cleared.Application.Invoices;

public sealed record IssueInvoiceRequest(DateOnly IssueDate, DateOnly DueDate);
