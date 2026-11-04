using System.Net.Sockets;
using Cleared.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Cleared.Infrastructure.Email;

// Sends one message over its own connection. A mail server's reply often repeats the address it refused
// and a log is readable by many people, so every failure leaves here as a fixed sentence, with no inner
// exception to carry the server's words.
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly string _domain;

    public SmtpEmailSender(IOptions<SmtpOptions> options)
    {
        _options = options.Value;
        _domain = _options.From[(_options.From.LastIndexOf('@') + 1)..];
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            using var mime = Build(message);

            // The library would otherwise name this machine in the hello and in the message id.
            using var client = new SmtpClient
            {
                Timeout = (int)_options.Timeout.TotalMilliseconds,
                LocalDomain = _domain,
            };

            await client.ConnectAsync(_options.Host, _options.Port, Secure(_options.TlsMode), cancellationToken);

            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await SayGoodbyeAsync(client, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new EmailDeliveryException(Describe(exception));
        }
    }

    private MimeMessage Build(EmailMessage message)
    {
        var mime = new MimeMessage
        {
            Subject = message.Subject,
            Body = new TextPart("plain") { Text = message.Body },
            MessageId = $"{Guid.NewGuid():N}@{_domain}",
        };
        mime.From.Add(new MailboxAddress(_options.FromName, _options.From));
        mime.To.Add(MailboxAddress.Parse(message.To));

        return mime;
    }

    private static SecureSocketOptions Secure(SmtpTlsMode mode) => mode switch
    {
        SmtpTlsMode.None => SecureSocketOptions.None,
        SmtpTlsMode.StartTls => SecureSocketOptions.StartTls,
        SmtpTlsMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => throw new InvalidOperationException("Email:Smtp:TlsMode is not a known mode."),
    };

    private static async Task SayGoodbyeAsync(SmtpClient client, CancellationToken cancellationToken)
    {
        try
        {
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The server has already taken the message, so a failed goodbye is not a failed delivery.
        }
    }

    private static string Describe(Exception exception) => exception switch
    {
        AuthenticationException => "The SMTP server refused the username and password.",
        SmtpCommandException command => $"The SMTP server refused a command with status {(int)command.StatusCode}.",
        SslHandshakeException => "The secure connection to the SMTP server could not be set up.",
        NotSupportedException => "The SMTP server does not offer what Email:Smtp asks for, such as TLS or sign-in.",
        TimeoutException or IOException { InnerException: SocketException { SocketErrorCode: SocketError.TimedOut } } =>
            "The SMTP server took too long to answer.",
        SocketException => "The SMTP server could not be reached.",
        _ => $"The message could not be delivered ({exception.GetType().Name}).",
    };
}
