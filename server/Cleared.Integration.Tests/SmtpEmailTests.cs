using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Cleared.Application.Abstractions;
using Cleared.Application.Auth;
using Cleared.Infrastructure.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cleared.Integration.Tests;

// The real sender against a real SMTP server. The rest of the suite swaps the sender for a recording one,
// so this is where the encoding, the headers and the link are proven to survive the trip.
[Collection(ApiCollection.Name)]
public class SmtpEmailTests(ClearedApiFactory factory, MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private const string _newPassword = "Brand-New-Password-2!";

    [Fact]
    public async Task A_reset_request_is_delivered_over_smtp_and_the_emailed_link_resets_the_password()
    {
        using var host = SmtpHost();
        using var client = host.CreateAnonymousClient();
        using var shared = factory.CreateAnonymousClient();
        var session = await SessionTestSupport.RegisterAsync(shared);

        using var asked = await client.PostAsJsonAsync(
            "api/v1/auth/forgot-password", new ForgotPasswordRequest(session.Email), ApiJson.Options);
        var mail = Assert.Single(await mailpit.WaitForAsync(session.Email));
        var token = EmailTestSupport.TokenIn(new EmailMessage(session.Email, mail.Subject, mail.Text));
        using var reset = await client.PostAsJsonAsync(
            "api/v1/auth/reset-password", new ResetPasswordRequest(token, _newPassword), ApiJson.Options);
        using var signIn = await client.PostAsJsonAsync(
            "api/v1/auth/login", new LoginRequest(session.Email, _newPassword), ApiJson.Options);

        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.Equal("Reset your Cleared password", mail.Subject);
        Assert.Equal(SmtpTestSupport.From, mail.FromAddress);
        Assert.Equal("Cleared", mail.FromName);
        Assert.Contains($"http://localhost:4200/reset-password#{token}", mail.Text, StringComparison.Ordinal);
        Assert.Equal(43, token.Length);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    [Fact]
    public async Task A_message_arrives_whole_with_accents_a_long_link_and_a_message_id_in_our_own_domain()
    {
        var to = $"zoe-{Guid.NewGuid():N}@example.test";
        var body = "Hello Zoë Müller,\n\nyour invoice for €1 250 and R1 250,00 is ready:\n\n"
            + $"http://localhost:4200/reset-password#{new string('x', 43)}\n\nThanks";

        await Sender().SendAsync(new EmailMessage(to, "Zoë's invoice", body), CancellationToken.None);

        var mail = Assert.Single(await mailpit.WaitForAsync(to));
        Assert.Equal("Zoë's invoice", mail.Subject);
        Assert.Equal(body, mail.Text.Replace("\r\n", "\n").TrimEnd('\n'));
        Assert.Matches(@"(?im)^Content-Type: text/plain; charset=""?utf-8""?", mail.Raw);
        Assert.Matches(@"(?im)^Message-ID: <[0-9a-f]{32}@cleared\.test>\s*$", mail.Raw);
    }

    [Fact]
    public async Task A_server_without_tls_is_refused_when_tls_is_required_and_nothing_is_delivered()
    {
        var to = $"nobody-{Guid.NewGuid():N}@example.test";
        var sender = Sender(options => options.TlsMode = SmtpTlsMode.StartTls);

        var failure = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            sender.SendAsync(new EmailMessage(to, "Secret subject", "Secret body"), CancellationToken.None));

        Assert.Equal("The SMTP server does not offer what Email:Smtp asks for, such as TLS or sign-in.", failure.Message);
        Assert.Empty(await mailpit.MessagesToAsync(to));
    }

    [Fact]
    public void A_host_without_a_from_address_stops_the_api_from_starting()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("Email:Smtp:Host", "smtp.example.test"));

        var failure = Record.Exception(() => host.CreateClient());

        Assert.NotNull(failure);
        Assert.Contains("Email:Smtp:From", StartupFailure.Messages(failure));
    }

    private WebApplicationFactory<Program> SmtpHost() => factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Email:Smtp:Host", mailpit.Host);
        builder.UseSetting("Email:Smtp:Port", mailpit.SmtpPort.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("Email:Smtp:TlsMode", "None");
        builder.UseSetting("Email:Smtp:From", SmtpTestSupport.From);
        builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IEmailSender, SmtpEmailSender>()));
    });

    private SmtpEmailSender Sender(Action<SmtpOptions>? change = null) =>
        SmtpTestSupport.SenderFor(mailpit.Host, mailpit.SmtpPort, change);
}
