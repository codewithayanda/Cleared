namespace Cleared.Domain.Invoicing;

// Zero-rated and exempt both charge 0 VAT but are not the same: zero-rated supplies
// (exports, basic foodstuffs) count toward taxable turnover, exempt supplies (financial
// services, residential rent) do not. One "no VAT" flag would be wrong on the VAT201.
public enum VatTreatment
{
    Standard,
    ZeroRated,
    Exempt,

    // For a line on an invoice from a tenant that is not VAT registered (see
    // Tenant.VatStatus). Such a tenant charges no VAT on anything, by law.
    NotApplicable,
}
