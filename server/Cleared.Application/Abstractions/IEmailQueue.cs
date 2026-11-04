namespace Cleared.Application.Abstractions;

// Hands a message over to be sent after the response has gone, so an answer never waits for the mail
// server and never shows whether a message was sent. False means the queue was full and the message
// was dropped.
public interface IEmailQueue
{
    bool TryEnqueue(EmailMessage message);
}
