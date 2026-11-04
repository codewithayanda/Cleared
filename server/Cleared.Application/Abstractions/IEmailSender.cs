namespace Cleared.Application.Abstractions;

// The one way the app sends an email. The development sender prints it and the real ones send it.
// A message is plain text, so there is no markup to escape.
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string To, string Subject, string Body);
