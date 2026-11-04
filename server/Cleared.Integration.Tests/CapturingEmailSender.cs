using Cleared.Application.Abstractions;
using Cleared.Infrastructure.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Cleared.Integration.Tests;

// Stands in for the real sender, so a test can read what the API would have emailed.
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        lock (_sent)
        {
            _sent.Add(message);
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> SentTo(string address)
    {
        lock (_sent)
        {
            return _sent
                .Where(message => string.Equals(message.To, address, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}

public static class EmailTestSupport
{
    public const string ConfirmationSubject = "Confirm your Cleared email address";

    private const string _resetMarker = "/reset-password#";
    private const string _confirmMarker = "/verify-email#";

    // Mail about passwords. Signing up also sends a confirmation link, which ConfirmationsToAsync reads,
    // so a test about one kind of mail counts only that kind.
    public static async Task<IReadOnlyList<EmailMessage>> SentToAsync(
        this ClearedApiFactory factory, string address, WebApplicationFactory<Program>? host = null) =>
        (await AllSentToAsync(factory, address, host)).Where(message => message.Subject != ConfirmationSubject).ToList();

    public static async Task<IReadOnlyList<EmailMessage>> ConfirmationsToAsync(
        this ClearedApiFactory factory, string address, WebApplicationFactory<Program>? host = null) =>
        (await AllSentToAsync(factory, address, host)).Where(message => message.Subject == ConfirmationSubject).ToList();

    // The secret in the emailed link. It sits after the #, so it never reaches a server log.
    public static string TokenIn(EmailMessage message) => SecretAfter(message, _resetMarker);

    public static string ConfirmationTokenIn(EmailMessage message) => SecretAfter(message, _confirmMarker);

    // Mail leaves after the response, so wait until the queue is empty before reading what was sent.
    private static async Task<IReadOnlyList<EmailMessage>> AllSentToAsync(
        ClearedApiFactory factory, string address, WebApplicationFactory<Program>? host)
    {
        await (host ?? factory).Services.GetRequiredService<EmailQueue>().WaitUntilIdleAsync(TimeSpan.FromSeconds(10));

        return factory.Emails.SentTo(address);
    }

    private static string SecretAfter(EmailMessage message, string marker)
    {
        var start = message.Body.IndexOf(marker, StringComparison.Ordinal) + marker.Length;

        return message.Body[start..].Split('\n', '\r', ' ')[0];
    }
}
