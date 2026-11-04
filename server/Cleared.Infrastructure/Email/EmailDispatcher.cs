using Cleared.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cleared.Infrastructure.Email;

// Sends the queued messages one at a time, after the response that asked for them has gone. A message
// the sender cannot send is dropped and logged, and the next one still goes.
public sealed partial class EmailDispatcher(EmailQueue queue, IEmailSender sender, ILogger<EmailDispatcher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await sender.SendAsync(message, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }
            finally
            {
                queue.Finished();
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "An email could not be sent and was dropped.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
