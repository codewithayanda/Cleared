using Cleared.Domain.Common;

namespace Cleared.Domain.Tests.Common;

public class EmailAddressRuleTests
{
    [Theory]
    [InlineData("jane@example.co.za")]
    [InlineData("jane.doe@example.com")]
    [InlineData("jane+invoices@example.com")]
    [InlineData("o'brien@example.co.za")]
    [InlineData("a_b-c@sub.example.org")]
    [InlineData("1234567890@example.com")]
    [InlineData("x@y.zz")]
    [InlineData("jane@xn--80ak6aa92e.com")]
    public void IsValid_RealAddress_ReturnsTrue(string email)
    {
        Assert.True(EmailAddressRule.IsValid(email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-an-email")]
    [InlineData("jane@example")]
    [InlineData("jane@localhost")]
    [InlineData("@example.com")]
    [InlineData("jane@")]
    [InlineData("jane@@example.com")]
    [InlineData("jane@home@example.com")]
    [InlineData("jane doe@example.com")]
    [InlineData(" jane@example.com")]
    [InlineData("jane@example.com ")]
    [InlineData("jane@example.com\n")]
    [InlineData("jane@example..com")]
    [InlineData("jane@.example.com")]
    [InlineData("jane@example.com.")]
    [InlineData(".jane@example.com")]
    [InlineData("jane.@example.com")]
    [InlineData("ja..ne@example.com")]
    [InlineData("jane@-example.com")]
    [InlineData("jane@example-.com")]
    [InlineData("jane@example.c")]
    [InlineData("jane@192.168.1.100")]
    [InlineData("jane@[192.168.1.100]")]
    [InlineData("josé@example.com")]
    [InlineData("jane@exämple.com")]
    [InlineData("\"jane doe\"@example.com")]
    [InlineData("jane,doe@example.com")]
    [InlineData("jane(doe)@example.com")]
    public void IsValid_NotAnAddress_ReturnsFalse(string? email)
    {
        Assert.False(EmailAddressRule.IsValid(email));
    }

    [Fact]
    public void AllowedCharacters_IsExactlyWhatTheRuleAcceptsInTheLocalPart()
    {
        for (var code = 0; code < 128; code++)
        {
            var c = (char)code;
            if (c == '@')
            {
                continue;
            }

            var accepted = EmailAddressRule.IsValid($"a{c}b@example.com");
            var allowed = EmailAddressRule.AllowedCharacters.Contains(c);

            Assert.True(accepted == allowed, $"Character code {code}: IsValid says {accepted}, AllowedCharacters says {allowed}.");
        }
    }

    [Fact]
    public void IsValid_ExactlyTheMaximumLength_ReturnsTrue()
    {
        var email = AddressOfLength(EmailAddressRule.MaxLength);

        Assert.Equal(EmailAddressRule.MaxLength, email.Length);
        Assert.True(EmailAddressRule.IsValid(email));
    }

    [Fact]
    public void IsValid_OneOverTheMaximumLength_ReturnsFalse()
    {
        var email = AddressOfLength(EmailAddressRule.MaxLength + 1);

        Assert.Equal(EmailAddressRule.MaxLength + 1, email.Length);
        Assert.False(EmailAddressRule.IsValid(email));
    }

    [Fact]
    public void IsValid_LocalPartLimit_Is64Characters()
    {
        Assert.True(EmailAddressRule.IsValid(new string('a', 64) + "@example.com"));
        Assert.False(EmailAddressRule.IsValid(new string('a', 65) + "@example.com"));
    }

    [Fact]
    public void IsValid_DomainLabelLimit_Is63Characters()
    {
        Assert.True(EmailAddressRule.IsValid("jane@" + new string('b', 63) + ".com"));
        Assert.False(EmailAddressRule.IsValid("jane@" + new string('b', 64) + ".com"));
    }

    // A 64 character local part, two 63 character labels, a last label that takes up the slack, then ".com".
    private static string AddressOfLength(int length)
    {
        const string topLevel = ".com";
        var localPart = new string('a', 64);
        var slack = length - localPart.Length - 1 - 63 - 1 - 63 - 1 - topLevel.Length;

        return $"{localPart}@{new string('b', 63)}.{new string('c', 63)}.{new string('d', slack)}{topLevel}";
    }
}
