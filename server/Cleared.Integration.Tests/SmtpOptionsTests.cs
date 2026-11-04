using Cleared.Infrastructure.Email;

namespace Cleared.Integration.Tests;

// The rules the API checks before it starts, so a bad setting stops it at once and not at the first reset.
public class SmtpOptionsTests
{
    [Fact]
    public void A_host_and_a_from_address_are_enough_to_start()
    {
        Assert.Empty(Problems(Valid()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void A_port_outside_the_range_is_refused(int port)
    {
        var problem = Assert.Single(Problems(Valid(options => options.Port = port)));

        Assert.Contains("Email:Smtp:Port", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("nobody@localhost")]
    public void A_from_address_that_cannot_be_real_is_refused(string from)
    {
        var problem = Assert.Single(Problems(Valid(options => options.From = from)));

        Assert.Contains("Email:Smtp:From", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_username_and_a_password_go_together()
    {
        var withoutPassword = Assert.Single(Problems(Valid(options => options.Username = "mailer")));
        var withoutUsername = Assert.Single(Problems(Valid(options => options.Password = "secret")));

        Assert.Contains("go together", withoutPassword, StringComparison.Ordinal);
        Assert.Contains("go together", withoutUsername, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SmtpTlsMode.None, false)]
    [InlineData(SmtpTlsMode.StartTls, true)]
    [InlineData(SmtpTlsMode.SslOnConnect, true)]
    public void A_password_is_only_allowed_where_it_will_not_cross_the_network_in_the_clear(SmtpTlsMode mode, bool allowed)
    {
        var problems = Problems(Valid(options =>
        {
            options.Username = "mailer";
            options.Password = "secret";
            options.TlsMode = mode;
        }));

        Assert.Equal(allowed, problems.Count == 0);
        Assert.All(problems, problem => Assert.Contains("Email:Smtp:TlsMode", problem, StringComparison.Ordinal));
    }

    [Fact]
    public void A_tls_mode_that_does_not_exist_is_refused()
    {
        var problem = Assert.Single(Problems(Valid(options => options.TlsMode = (SmtpTlsMode)7)));

        Assert.Contains("Email:Smtp:TlsMode", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(180)]
    public void A_timeout_that_is_zero_or_too_long_is_refused(int seconds)
    {
        var problem = Assert.Single(Problems(Valid(options => options.Timeout = TimeSpan.FromSeconds(seconds))));

        Assert.Contains("Email:Smtp:Timeout", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_options_never_print_the_password()
    {
        var options = Valid(change => change.Password = "S3cret-Smtp-Passw0rd!");

        Assert.DoesNotContain("S3cret-Smtp-Passw0rd!", options.ToString(), StringComparison.Ordinal);
    }

    private static SmtpOptions Valid(Action<SmtpOptions>? change = null)
    {
        var options = new SmtpOptions { Host = "smtp.example.test", From = "no-reply@cleared.test" };
        change?.Invoke(options);

        return options;
    }

    private static List<string> Problems(SmtpOptions options) =>
        [.. new SmtpOptionsValidator().Validate(null, options).Failures ?? []];
}
