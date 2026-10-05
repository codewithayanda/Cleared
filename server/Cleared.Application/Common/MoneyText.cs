using System.Globalization;

namespace Cleared.Application.Common;

// Parses the money strings that arrive on the wire. Strict on purpose: under the default
// number styles "1,50" reads as 150, because the comma becomes a thousands separator.
public static class MoneyText
{
    private const NumberStyles Styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    // Throws ArgumentException, which the API maps to 400, rather than the FormatException
    // or OverflowException that decimal.Parse throws and the API would report as a 500.
    public static decimal Parse(string? text, string what)
    {
        if (!decimal.TryParse(text, Styles, CultureInfo.InvariantCulture, out var value))
        {
            throw new ArgumentException($"{what} must be a plain decimal number such as 150.00.");
        }

        return value;
    }
}
