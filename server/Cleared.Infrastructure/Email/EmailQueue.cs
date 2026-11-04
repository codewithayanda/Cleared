using System.Threading.Channels;
using Cleared.Application.Abstractions;

namespace Cleared.Infrastructure.Email;

// A bounded line of messages waiting to be sent. A flood of requests fills the line and the rest are
// dropped, so memory stays flat and the mail server is never hammered.
public sealed class EmailQueue : IEmailQueue
{
    public const int Capacity = 100;

    private readonly Channel<EmailMessage> _line = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    private int _waiting;

    internal ChannelReader<EmailMessage> Reader => _line.Reader;

    public bool TryEnqueue(EmailMessage message)
    {
        Interlocked.Increment(ref _waiting);

        if (_line.Writer.TryWrite(message))
        {
            return true;
        }

        Interlocked.Decrement(ref _waiting);

        return false;
    }

    // Called once a message has been through the sender, whether or not it was sent.
    internal void Finished() => Interlocked.Decrement(ref _waiting);

    // For tests, which need to know that everything handed over has been through the sender.
    public async Task WaitUntilIdleAsync(TimeSpan timeout)
    {
        using var giveUp = new CancellationTokenSource(timeout);

        while (Volatile.Read(ref _waiting) > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), giveUp.Token);
        }
    }
}
