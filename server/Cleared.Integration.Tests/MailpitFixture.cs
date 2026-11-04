using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Cleared.Integration.Tests;

public sealed record MailpitMessage(
    string FromAddress, string FromName, IReadOnlyList<string> To, string Subject, string Text, string Raw);

// A throwaway Mailpit: a real SMTP server that keeps every message and shows it through an HTTP API, so
// a test can read back what really went over the wire.
public sealed class MailpitFixture : IAsyncLifetime
{
    private const int _smtpContainerPort = 1025;
    private const int _httpContainerPort = 8025;

    private readonly IContainer _container = new ContainerBuilder("axllent/mailpit:v1.33.0")
        .WithPortBinding(_smtpContainerPort, true)
        .WithPortBinding(_httpContainerPort, true)
        .Build();

    private readonly HttpClient _api = new();

    // "localhost" resolves to IPv6 first and stalls against the mapped port here.
    public string Host => _container.Hostname == "localhost" ? "127.0.0.1" : _container.Hostname;

    public int SmtpPort => _container.GetMappedPublicPort(_smtpContainerPort);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _api.BaseAddress = new Uri($"http://{Host}:{_container.GetMappedPublicPort(_httpContainerPort)}/");
        await WaitUntilReadyAsync();
    }

    public async Task DisposeAsync()
    {
        _api.Dispose();
        await _container.DisposeAsync();
    }

    // Mail is sent after the response, so a test waits here for what it expects to arrive.
    public async Task<IReadOnlyList<MailpitMessage>> WaitForAsync(string address, int count = 1)
    {
        var giveUp = DateTimeOffset.UtcNow.AddSeconds(20);

        while (true)
        {
            var found = await MessagesToAsync(address);

            if (found.Count >= count || DateTimeOffset.UtcNow > giveUp)
            {
                return found;
            }

            await Task.Delay(100);
        }
    }

    public async Task<IReadOnlyList<MailpitMessage>> MessagesToAsync(string address)
    {
        using var list = JsonDocument.Parse(await _api.GetStringAsync("api/v1/messages?limit=500"));
        var found = new List<MailpitMessage>();

        foreach (var summary in list.RootElement.GetProperty("messages").EnumerateArray())
        {
            var recipients = summary.GetProperty("To").EnumerateArray()
                .Select(to => to.GetProperty("Address").GetString()!)
                .ToList();

            if (!recipients.Contains(address, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var id = summary.GetProperty("ID").GetString()!;
            using var detail = JsonDocument.Parse(await _api.GetStringAsync($"api/v1/message/{id}"));
            var from = detail.RootElement.GetProperty("From");

            found.Add(new MailpitMessage(
                from.GetProperty("Address").GetString()!,
                from.GetProperty("Name").GetString()!,
                recipients,
                detail.RootElement.GetProperty("Subject").GetString()!,
                detail.RootElement.GetProperty("Text").GetString()!,
                await _api.GetStringAsync($"api/v1/message/{id}/raw")));
        }

        return found;
    }

    private async Task WaitUntilReadyAsync()
    {
        var giveUp = DateTimeOffset.UtcNow.AddSeconds(40);

        while (true)
        {
            try
            {
                using var attempt = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var response = await _api.GetAsync("api/v1/messages", attempt.Token);

                if (response.IsSuccessStatusCode && await SmtpGreetsAsync(attempt.Token))
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or SocketException or OperationCanceledException)
            {
                // Not up yet.
            }

            if (DateTimeOffset.UtcNow > giveUp)
            {
                throw new InvalidOperationException("Mailpit did not start in time.");
            }

            await Task.Delay(250);
        }
    }

    private async Task<bool> SmtpGreetsAsync(CancellationToken cancellationToken)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(Host, SmtpPort, cancellationToken);
        using var reader = new StreamReader(tcp.GetStream(), Encoding.ASCII);

        return (await reader.ReadLineAsync(cancellationToken))?.StartsWith("220", StringComparison.Ordinal) == true;
    }
}
