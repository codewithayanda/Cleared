using Cleared.Infrastructure.Email;
using Microsoft.Extensions.Options;

namespace Cleared.Integration.Tests;

public static class SmtpTestSupport
{
    public const string From = "no-reply@cleared.test";

    // A real sender pointed at a test server, with no encryption because a test server has no certificate.
    public static SmtpEmailSender SenderFor(string host, int port, Action<SmtpOptions>? change = null)
    {
        var options = new SmtpOptions
        {
            Host = host,
            Port = port,
            TlsMode = SmtpTlsMode.None,
            From = From,
            Timeout = TimeSpan.FromSeconds(5),
        };
        change?.Invoke(options);

        return new SmtpEmailSender(Options.Create(options));
    }
}
