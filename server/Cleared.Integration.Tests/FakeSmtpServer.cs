using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Cleared.Integration.Tests;

// A tiny SMTP server that answers each line with whatever the test says. It lets a refusal repeat the
// recipient or the user name, so a test can check that none of it comes back out of the sender.
public sealed class FakeSmtpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<string, string?> _reply;
    private readonly List<string> _received = [];
    private readonly Task _running;

    // The reply may hold several lines. Null says nothing, which is how a test makes the server hang.
    public FakeSmtpServer(Func<string, string?> reply, bool greet = true)
    {
        _reply = reply;
        _listener.Start();
        _running = Task.Run(() => AcceptAsync(greet));
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public IReadOnlyList<string> Received
    {
        get
        {
            lock (_received)
            {
                return [.. _received];
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _ = _running.Wait(TimeSpan.FromSeconds(2));
        _stop.Dispose();
    }

    private async Task AcceptAsync(bool greet)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await ServeAsync(client, greet);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // The test is over and the listener was closed.
        }
    }

    private async Task ServeAsync(TcpClient client, bool greet)
    {
        try
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

            if (greet)
            {
                await writer.WriteLineAsync("220 fake ESMTP ready");
            }

            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                lock (_received)
                {
                    _received.Add(line);
                }

                if (_reply(line) is { } answer)
                {
                    await writer.WriteLineAsync(answer);
                }
            }
        }
        catch (IOException)
        {
            // The sender hung up, which is what it does when it gives up or has finished.
        }
    }
}
