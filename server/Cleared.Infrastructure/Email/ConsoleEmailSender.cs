using Cleared.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Cleared.Infrastructure.Email;

// For development only: prints the whole message, link included, where the API's output shows it.
public sealed partial class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        LogPrinted(logger, message.To, message.Subject, message.Body);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Email printed, not sent. To: {To}. Subject: {Subject}.\n{Body}")]
    private static partial void LogPrinted(ILogger logger, string to, string subject, string body);
}
