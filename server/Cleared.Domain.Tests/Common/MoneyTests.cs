using System.Globalization;
using Cleared.Domain.Common;

namespace Cleared.Domain.Tests.Common;

public class MoneyTests
{
    [Fact]
    public void Zar_SetsAmountAndCurrency()
    {
        var money = Money.Zar(12.34m);

        Assert.Equal(12.34m, money.Amount);
        Assert.Equal(CurrencyCode.Zar, money.Currency);
    }

    [Theory]
    [InlineData("1.005", "1.01")]
    [InlineData("1.004", "1.00")]
    [InlineData("-1.005", "-1.01")]
    public void Zar_RoundsToCents_AwayFromZero(string input, string expected)
    {
        var actual = Money.Zar(decimal.Parse(input, CultureInfo.InvariantCulture)).Amount;

        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), actual);
    }

    [Fact]
    public void Zero_IsZarZero()
    {
        var zero = Money.Zero;

        Assert.Equal(0m, zero.Amount);
        Assert.Equal(CurrencyCode.Zar, zero.Currency);
    }

    [Fact]
    public void Add_SameCurrency_ReturnsSum()
    {
        var result = Money.Zar(10.50m).Add(Money.Zar(4.25m));

        Assert.Equal(14.75m, result.Amount);
        Assert.Equal(CurrencyCode.Zar, result.Currency);
    }

    [Fact]
    public void Subtract_SameCurrency_ReturnsDifference()
    {
        var result = Money.Zar(10.50m).Subtract(Money.Zar(4.25m));

        Assert.Equal(6.25m, result.Amount);
        Assert.Equal(CurrencyCode.Zar, result.Currency);
    }

    [Fact]
    public void MultiplyBy_ScalesAmount_AndRoundsToCents()
    {
        var result = Money.Zar(10.00m).MultiplyBy(1.5m);

        Assert.Equal(15.00m, result.Amount);
        Assert.Equal(CurrencyCode.Zar, result.Currency);
    }

    [Fact]
    public void MultiplyBy_RoundsResult_AwayFromZero()
    {
        var result = Money.Zar(1.00m).MultiplyBy(1.005m);

        Assert.Equal(1.01m, result.Amount);
    }

    [Fact]
    public void Add_Subtract_Multiply_ByZero()
    {
        Assert.Equal(5m, Money.Zar(5m).Add(Money.Zero).Amount);
        Assert.Equal(5m, Money.Zar(5m).Subtract(Money.Zero).Amount);
        Assert.Equal(0m, Money.Zar(5m).MultiplyBy(0m).Amount);
    }

    [Fact]
    public void Equality_SameAmountAndCurrency_AreEqual()
    {
        Assert.Equal(Money.Zar(9.99m), Money.Zar(9.99m));
    }

    [Fact]
    public void Equality_DifferentAmount_AreNotEqual()
    {
        Assert.NotEqual(Money.Zar(9.99m), Money.Zar(10.00m));
    }
}
