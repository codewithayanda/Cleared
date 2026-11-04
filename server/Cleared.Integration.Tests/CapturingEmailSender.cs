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
    private const string LinkMarker = "/reset-password#";

    // Mail leaves after the response, so wait until the queue is empty before reading what was sent.
    public static async Task<IReadOnlyList<EmailMessage>> SentToAsync(
        this ClearedApiFactory factory, string address, WebApplicationFactory<Program>? host = null)
    {
        await (host ?? factory).Services.GetRequiredService<EmailQueue>().WaitUntilIdleAsync(TimeSpan.FromSeconds(10));

        return factory.Emails.SentTo(address);
    }

    // The secret in the emailed link. It sits after the #, so it never reaches a server log.
    public static string TokenIn(EmailMessage message)
    {
        var start = message.Body.IndexOf(LinkMarker, StringComparison.Ordinal) + LinkMarker.Length;

        return message.Body[start..].Split('\n', '\r', ' ')[0];
    }
}
