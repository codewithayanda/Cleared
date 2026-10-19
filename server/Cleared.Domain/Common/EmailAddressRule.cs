namespace Cleared.Domain.Common;

// One answer to "is this a usable email address" for every place one is accepted. Stricter than
// the RFC on purpose: ASCII only, no quoted names or address literals, and a dotted domain.
// The form check in client/src/app/core/utils/email-rule.ts must say the same thing.
public static class EmailAddressRule
{
    public const int MaxLength = 254;

    // Identity checks the username, which is always the email, against a list of its own that
    // refuses o'brien@example.co.za. Handing it this list lets the rule above decide instead.
    public const string AllowedCharacters =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.@" + LocalPartSymbols;

    private const int MaxLocalPartLength = 64;
    private const int MaxLabelLength = 63;
    private const string LocalPartSymbols = "!#$%&'*+/=?^_`{|}~-";

    public static bool IsValid(string? value)
    {
        if (value is null || value.Length > MaxLength)
        {
            return false;
        }

        var at = value.IndexOf('@');
        if (at < 1 || at != value.LastIndexOf('@'))
        {
            return false;
        }

        return IsValidLocalPart(value[..at]) && IsValidDomain(value[(at + 1)..]);
    }

    private static bool IsValidLocalPart(string localPart) =>
        localPart.Length <= MaxLocalPartLength
        && localPart[0] != '.'
        && localPart[^1] != '.'
        && !localPart.Contains("..", StringComparison.Ordinal)
        && localPart.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || LocalPartSymbols.Contains(c));

    private static bool IsValidDomain(string domain)
    {
        var labels = domain.Split('.');

        return labels.Length >= 2 && labels.All(IsValidLabel) && IsValidTopLevelDomain(labels[^1]);
    }

    private static bool IsValidLabel(string label) =>
        label.Length is >= 1 and <= MaxLabelLength
        && label[0] != '-'
        && label[^1] != '-'
        && label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    // A one-letter ending is a truncated address, and an all-digit one is an IP address typed bare.
    private static bool IsValidTopLevelDomain(string label) =>
        label.Length >= 2 && !label.All(char.IsAsciiDigit);
}
