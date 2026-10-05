using Cleared.Application.Common;

namespace Cleared.Application.Tests.Common;

public class MoneyTextTests
{
    [Theory]
    [InlineData("150.00", "150.00")]
    [InlineData("100", "100")]
    [InlineData("0.5", "0.5")]
    [InlineData(".5", "0.5")]
    [InlineData("-10.25", "-10.25")]
    public void Parse_AcceptsAPlainDecimal(string text, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), MoneyText.Parse(text, "Amount"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData(" 100")]
    [InlineData("100 ")]
    [InlineData("1e3")]
    [InlineData("99999999999999999999999999999")]
    public void Parse_RejectsWhatIsNotADecimal(string? text)
    {
        var error = Assert.Throws<ArgumentException>(() => MoneyText.Parse(text, "Amount"));

        Assert.StartsWith("Amount must be", error.Message, StringComparison.Ordinal);
    }

    // "1,50" is how a South African user writes one rand fifty. The default number styles
    // read the comma as a thousands separator and return 150, a hundredfold overcharge.
    [Theory]
    [InlineData("1,50")]
    [InlineData("1,000.50")]
    public void Parse_RejectsACommaInsteadOfReadingItAsThousands(string text)
    {
        Assert.Throws<ArgumentException>(() => MoneyText.Parse(text, "Amount"));
    }
}
