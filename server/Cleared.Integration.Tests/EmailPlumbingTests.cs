using Cleared.Application.Abstractions;
using Cleared.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cleared.Integration.Tests;

// The pieces behind the emails, without the API: the queue, the dispatcher, and the two senders that
// stand in until a real one is set up.
public class EmailPlumbingTests
{
    private static readonly EmailMessage Message = new(
        "someone@example.test", "Reset your Cleared password", "Open https://app.example.test/reset-password#SECRET-LINK now.");

    [Fact]
    public void The_queue_holds_a_fixed_number_of_messages_and_refuses_the_rest()
    {
        var queue = new EmailQueue();

        var accepted = Enumerable.Range(0, EmailQueue.Capacity).Select(_ => queue.TryEnqueue(Message)).ToList();

        Assert.All(accepted, taken => Assert.True(taken));
        Assert.False(queue.TryEnqueue(Message));
    }

    [Fact]
    public async Task A_message_the_sender_cannot_send_does_not_stop_the_ones_behind_it()
    {
        var queue = new EmailQueue();
        var sender = new FailsOnce();
        using var dispatcher = new EmailDispatcher(queue, sender, NullLogger<EmailDispatcher>.Instance);
        await dispatcher.StartAsync(CancellationToken.None);

        queue.TryEnqueue(Message with { Subject = "first" });
        queue.TryEnqueue(Message with { Subject = "second" });
        await queue.WaitUntilIdleAsync(TimeSpan.FromSeconds(10));
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.Equal(["first", "second"], sender.Attempted);
        Assert.Equal(["second"], sender.Delivered);
    }

    [Fact]
    public async Task The_development_sender_prints_the_whole_message_link_included()
    {
        var logger = new ListLogger<ConsoleEmailSender>();

        await new ConsoleEmailSender(logger).SendAsync(Message, CancellationToken.None);

        var printed = Assert.Single(logger.Lines);
        Assert.Contains("someone@example.test", printed, StringComparison.Ordinal);
        Assert.Contains("SECRET-LINK", printed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_sender_used_when_none_is_set_up_never_logs_the_address_the_subject_or_the_link()
    {
        var logger = new ListLogger<UnconfiguredEmailSender>();

        await new UnconfiguredEmailSender(logger).SendAsync(Message, CancellationToken.None);

        var line = Assert.Single(logger.Lines);
        Assert.DoesNotContain("someone@example.test", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Reset your Cleared password", line, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-LINK", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development", typeof(ConsoleEmailSender))]
    [InlineData("Production", typeof(UnconfiguredEmailSender))]
    [InlineData("Testing", typeof(UnconfiguredEmailSender))]
    public void Only_development_prints_emails(string environment, Type expected)
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddEmail(new FakeEnvironment(environment))
            .BuildServiceProvider();

        Assert.IsType(expected, provider.GetRequiredService<IEmailSender>());
        Assert.Same(provider.GetRequiredService<EmailQueue>(), provider.GetRequiredService<IEmailQueue>());
        Assert.Single(provider.GetServices<IHostedService>(), service => service is EmailDispatcher);
    }

    private sealed class FailsOnce : IEmailSender
    {
        public List<string> Attempted { get; } = [];

        public List<string> Delivered { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Attempted.Add(message.Subject);

            if (Attempted.Count == 1)
            {
                throw new InvalidOperationException("The mail server said no.");
            }

            Delivered.Add(message.Subject);

            return Task.CompletedTask;
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
