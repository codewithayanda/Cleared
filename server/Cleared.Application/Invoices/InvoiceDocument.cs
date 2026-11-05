using Cleared.Application.Customers;
using Cleared.Application.Tenants;

namespace Cleared.Application.Invoices;

// What an invoice PDF says, with every word decided where a test can read it, so the layout only has
// to draw it. Money and dates are already text, in a form that reads the same on any server.
public sealed record InvoiceDocument(
    string Title,
    string? Number,
    string? Mark,
    bool ShowVat,
    string? IssueDate,
    string? DueDate,
    string? VatRate,
    IReadOnlyList<InvoiceDocumentLine> Lines,
    string Subtotal,
    string VatTotal,
    string? InvoiceTotal,
    string? PaymentsHeading,
    IReadOnlyList<InvoiceDocumentPayment> Payments,
    string TotalLabel,
    string? TotalAmount,
    bool ShowBanking,
    CustomerResponse Customer,
    TenantResponse Tenant);

public sealed record InvoiceDocumentLine(
    string Description, string Quantity, string UnitPrice, string VatTreatment, string LineVat, string LineTotal);

public sealed record InvoiceDocumentPayment(string Date, string Amount);
