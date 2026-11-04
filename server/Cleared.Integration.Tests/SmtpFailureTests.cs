using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Cleared.Application.Abstractions;
using Cleared.Infrastructure.Email;

namespace Cleared.Integration.Tests;

// What the sender does when the server says no. A server's reply often repeats the address it refused,
// and a log is readable by many people, so every failure must come out as a fixed sentence.
public class SmtpFailureTests
{
    private const string _address = "owner@secret-customer.test";
    private const string _subject = "Reset your Cleared password";
    private const string _secretLink = "SECRET-LINK-TOKEN";
    private const string _user = "mailer@secret-provider.test";
    private const string _password = "S3cret-Smtp-Passw0rd!";
    private const string _serverText = "secret-server-policy";

    private static readonly EmailMessage _message = new(
        _address, _subject, $"Open https://app.example.test/reset-password#{_secretLink} now.");

    [Fact]
    public async Task The_hello_names_the_domain_of_the_sending_address_and_not_this_machine()
    {
        using var server = new FakeSmtpServer(_ => "421 4.3.0 go away");
        var sender = SmtpTestSupport.SenderFor("127.0.0.1", server.Port);

        await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(_message, CancellationToken.None));

        Assert.Equal("EHLO cleared.test", server.Received[0]);
    }

    [Fact]
    public async Task A_refused_recipient_fails_with_the_status_and_never_repeats_the_address_or_the_server_text()
    {
        using var server = new FakeSmtpServer(line =>
            Is(line, "EHLO") ? "250 fake"
            : Is(line, "RCPT") ? $"550 5.1.1 {line}: Recipient address rejected by {_serverText}"
            : Is(line, "QUIT") ? "221 bye"
            : "250 ok");
        var sender = SmtpTestSupport.SenderFor("127.0.0.1", server.Port);

        var failure = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(_message, CancellationToken.None));

        Assert.Equal("The SMTP server refused a command with status 550.", failure.Message);
        Assert.Contains(server.Received, line => Is(line, "RCPT") && line.Contains(_address, StringComparison.Ordinal));
        AssertNothingSecret(failure);
    }

    [Fact]
    public async Task A_refused_sign_in_fails_with_a_fixed_sentence_and_never_repeats_the_user_or_the_password()
    {
        using var server = new FakeSmtpServer(line =>
            Is(line, "EHLO") ? "250-fake\r\n250 AUTH PLAIN"
            : line == "AUTH PLAIN" ? "334 "
            : Is(line, "QUIT") ? "221 bye"
            : $"535 5.7.8 Authentication credentials invalid for {_user} ({_serverText})");
        var sender = SmtpTestSupport.SenderFor("127.0.0.1", server.Port, WithCredentials);

        var failure = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(_message, CancellationToken.None));

        Assert.Equal("The SMTP server refused the username and password.", failure.Message);
        AssertNothingSecret(failure);
    }

    [Fact]
    public async Task The_user_and_password_are_sent_when_they_are_set_and_not_when_they_are_not()
    {
        static string? Script(string line) =>
            Is(line, "EHLO") ? "250-fake\r\n250 AUTH PLAIN"
            : line == "AUTH PLAIN" ? "334 "
            : Is(line, "AUTH PLAIN ") ? "235 2.7.0 welcome"
            : Is(line, "MAIL") ? "550 5.7.1 stop here"
            : Is(line, "QUIT") ? "221 bye"
            : "250 ok";

        using var withCredentials = new FakeSmtpServer(Script);
        using var withoutCredentials = new FakeSmtpServer(Script);

        await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            SmtpTestSupport.SenderFor("127.0.0.1", withCredentials.Port, WithCredentials).SendAsync(_message, CancellationToken.None));
        await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            SmtpTestSupport.SenderFor("127.0.0.1", withoutCredentials.Port).SendAsync(_message, CancellationToken.None));

        var auth = Assert.Single(withCredentials.Received, line => Is(line, "AUTH PLAIN "));
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(auth["AUTH PLAIN ".Length..]));
        Assert.Equal($"\0{_user}\0{_password}", payload);
        Assert.Contains(withCredentials.Received, line => Is(line, "MAIL FROM"));
        Assert.DoesNotContain(withoutCredentials.Received, line => Is(line, "AUTH"));
    }

    [Fact]
    public async Task A_server_that_never_answers_is_given_up_on_after_the_timeout()
    {
        using var server = new FakeSmtpServer(_ => null, greet: false);
        var sender = SmtpTestSupport.SenderFor(
            "127.0.0.1", server.Port, options => options.Timeout = TimeSpan.FromSeconds(1));
        var clock = Stopwatch.StartNew();

        var sending = Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(_message, CancellationToken.None));
        var finished = await Task.WhenAny(sending, Task.Delay(TimeSpan.FromSeconds(15)));

        Assert.Same(sending, finished);
        Assert.Equal("The SMTP server took too long to answer.", (await sending).Message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_server_that_is_not_there_fails_with_a_plain_sentence()
    {
        var sender = SmtpTestSupport.SenderFor("127.0.0.1", FreePort());

        var failure = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(_message, CancellationToken.None));

        Assert.Equal("The SMTP server could not be reached.", failure.Message);
        AssertNothingSecret(failure);
    }

    [Fact]
    public async Task A_cancelled_send_is_cancelled_and_not_reported_as_a_failed_delivery()
    {
        using var server = new FakeSmtpServer(_ => null, greet: false);
        var sender = SmtpTestSupport.SenderFor(
            "127.0.0.1", server.Port, options => options.Timeout = TimeSpan.FromSeconds(30));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendAsync(_message, cancel.Token));
    }

    private static bool Is(string line, string verb) => line.StartsWith(verb, StringComparison.Ordinal);

    private static void WithCredentials(SmtpOptions options)
    {
        options.Username = _user;
        options.Password = _password;
    }

    private static void AssertNothingSecret(Exception failure)
    {
        var everything = failure.ToString();

        foreach (var secret in new[] { _address, _subject, _secretLink, _user, _password, _serverText })
        {
            Assert.DoesNotContain(secret, everything, StringComparison.Ordinal);
        }
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();

        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
