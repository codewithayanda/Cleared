using Cleared.Domain.Common;

namespace Cleared.Domain.Invoicing;

// National reference data, not tenant-scoped: every tenant charges the same statutory rate.
// Effective-dated because the rate changes over time. An invoice snapshots the rate that
// applied on its supply date, or old invoices would restate themselves on a rate change.
public sealed class VatRate : Entity
{
    public decimal Rate { get; }

    // Private setters, not constructor parameters. DateOnly hits the same EF Core
    // constructor-binding limit as DateTimeOffset. See InvoiceLineItem.UnitPrice.
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
                nameof(rate), rate, "A VAT rate is a fraction between 0 and 1: 15% is expressed as 0.15.");
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
