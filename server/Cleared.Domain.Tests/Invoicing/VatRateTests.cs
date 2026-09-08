using Cleared.Domain.Invoicing;

namespace Cleared.Domain.Tests.Invoicing;

public class VatRateTests
{
    [Fact]
    public void Create_RateBelowZero_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VatRate.Create(Guid.NewGuid(), -0.01m, new DateOnly(2018, 4, 1)));
    }

    [Fact]
    public void Create_RateAboveOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VatRate.Create(Guid.NewGuid(), 1.01m, new DateOnly(2018, 4, 1)));
    }

    [Fact]
    public void Create_EffectiveToBeforeEffectiveFrom_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            VatRate.Create(Guid.NewGuid(), 0.15m, new DateOnly(2018, 4, 1), new DateOnly(2018, 3, 1)));
    }

    [Fact]
    public void AppliesOn_DateWithinOpenEndedRange_ReturnsTrue()
    {
        var rate = VatRate.Create(Guid.NewGuid(), 0.15m, new DateOnly(2018, 4, 1));

        Assert.True(rate.AppliesOn(new DateOnly(2026, 8, 27)));
    }

    [Fact]
    public void AppliesOn_DateBeforeEffectiveFrom_ReturnsFalse()
    {
        var rate = VatRate.Create(Guid.NewGuid(), 0.15m, new DateOnly(2018, 4, 1));

        Assert.False(rate.AppliesOn(new DateOnly(2018, 3, 31)));
    }

    [Fact]
    public void AppliesOn_ExactlyOnEffectiveFrom_ReturnsTrue()
    {
        var rate = VatRate.Create(Guid.NewGuid(), 0.15m, new DateOnly(2018, 4, 1));

        Assert.True(rate.AppliesOn(new DateOnly(2018, 4, 1)));
    }

    [Fact]
    public void AppliesOn_DateAfterEffectiveTo_ReturnsFalse()
    {
        // Models the near-miss 2025 Budget scenario (C6): an old rate closed out the day
        // before a new one takes effect.
        var oldRate = VatRate.Create(Guid.NewGuid(), 0.15m, new DateOnly(2018, 4, 1), new DateOnly(2026, 3, 31));

        Assert.False(oldRate.AppliesOn(new DateOnly(2026, 4, 1)));
    }

    [Fact]
    public void AppliesOn_ExactlyOnEffectiveTo_ReturnsTrue()
    {
        var rate = VatRate.Create(Guid.NewGuid(), 0.15m, new DateOnly(2018, 4, 1), new DateOnly(2026, 3, 31));

        Assert.True(rate.AppliesOn(new DateOnly(2026, 3, 31)));
    }
}
