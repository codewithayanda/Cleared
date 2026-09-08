using Cleared.Domain.Common;

namespace Cleared.Domain.Invoicing;

// National reference data, not tenant-scoped — every tenant charges the same statutory
// rate. Effective-dated because the rate changes over time (and nearly did again in the
// 2025 Budget before being withdrawn): an invoice must snapshot the rate that applied on
// its supply date and never re-read "the current rate" after issue, or last year's
// invoices would silently restate themselves the day the rate changes.
public sealed class VatRate : Entity
{
    public decimal Rate { get; }

    // Not constructor parameters — see the note on Tenant.CreatedAt / InvoiceLineItem.UnitPrice.
    // DateOnly hits the same EF Core constructor-binding limitation there, not just DateTimeOffset.
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }

    private VatRate(Guid id, decimal rate) : base(id)
    {
        Rate = rate;
    }

    public static VatRate Create(Guid id, decimal rate, DateOnly effectiveFrom, DateOnly? effectiveTo = null)
    {
        if (rate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rate), rate, "A VAT rate is a fraction between 0 and 1 — 15% is expressed as 0.15.");
        }

        if (effectiveTo is { } endDate && endDate < effectiveFrom)
        {
            throw new ArgumentException(
                "The effective-to date cannot be before the effective-from date.", nameof(effectiveTo));
        }

        return new VatRate(id, rate)
        {
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
        };
    }

    public bool AppliesOn(DateOnly date) => date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);
}
