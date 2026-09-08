namespace Cleared.Domain.Invoicing;

// Zero-rated and exempt both charge 0 VAT, but they are not the same thing: zero-rated
// supplies (exports, basic foodstuffs) still count toward taxable turnover, exempt
// supplies (financial services, residential rent) do not. Collapsing them into one
// "no VAT" flag would be indistinguishable in the arithmetic and wrong on the VAT201.
public enum VatTreatment
{
    Standard,
    ZeroRated,
    Exempt,

    // For a line on an invoice issued by a tenant that is not VAT registered — see
    // Tenant.VatStatus. Such a tenant charges no VAT on anything, by law, not by choice.
    NotApplicable,
}
