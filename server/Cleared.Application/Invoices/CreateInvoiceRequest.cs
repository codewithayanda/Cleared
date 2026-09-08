namespace Cleared.Application.Invoices;

public sealed record CreateInvoiceLineRequest(string Description, decimal Quantity, string UnitPrice);

public sealed record CreateInvoiceRequest(Guid CustomerId, IReadOnlyList<CreateInvoiceLineRequest> Lines);
