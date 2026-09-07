namespace Cleared.Domain.Common;

public readonly record struct Money
{
    public decimal Amount { get; }
    public CurrencyCode Currency { get; }

    private Money(decimal amount, CurrencyCode currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Zar(decimal amount)
    {
        return new Money(RoundToCents(amount), CurrencyCode.Zar);
    }

    public static Money Zero => Zar(0m);

    public Money Add(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new InvalidOperationException("Cannot add amounts with different currencies.");
        }
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new InvalidOperationException("Cannot subtract amounts with different currencies.");
        }
        return new Money(Amount - other.Amount, Currency);
    }

    public Money MultiplyBy(decimal factor)
    {
        return new Money(RoundToCents(Amount * factor), Currency);
    }

    private static decimal RoundToCents(decimal value)
    {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
