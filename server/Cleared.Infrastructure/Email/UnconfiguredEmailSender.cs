using Cleared.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Cleared.Infrastructure.Email;

// Used wherever no real sender is set up. It says a message was dropped and nothing more: a log is
// readable by many people, so it never carries the address, the subject or a link.
public sealed partial class UnconfiguredEmailSender(ILogger<UnconfiguredEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        LogDropped(logger);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "An email was dropped because no email sender is configured.")]
    private static partial void LogDropped(ILogger logger);
}
