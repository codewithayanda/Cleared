namespace Cleared.Domain.Invoicing;

// Who an invoice was made out to, and by, on the day it was issued. The invoice keeps these whatever
// happens to the customer or the company afterwards, because a tax invoice must read the same for ever.
public sealed record CustomerDetails(string Name, string? Address, string? VatNumber);

public sealed record SupplierDetails(string CompanyName, string? TradingName, string? Address, string? VatNumber);
