using Cleared.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Cleared.Infrastructure.Email;

public static class EmailServiceCollectionExtensions
{
    // Mail leaves after the response, one message at a time, from a bounded queue. SMTP settings choose
    // the real sender. Without them only development prints mail and everywhere else drops it, with a
    // warning.
    public static IServiceCollection AddEmail(
        this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        services.AddSingleton<EmailQueue>();
        services.AddSingleton<IEmailQueue>(provider => provider.GetRequiredService<EmailQueue>());
        services.AddHostedService<EmailDispatcher>();

        var smtp = configuration.GetSection(SmtpOptions.SectionName);

        if (!string.IsNullOrWhiteSpace(smtp[nameof(SmtpOptions.Host)]))
        {
            services.AddOptions<SmtpOptions>().Bind(smtp).ValidateOnStart();
            services.AddSingleton<IValidateOptions<SmtpOptions>, SmtpOptionsValidator>();
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else if (environment.IsDevelopment())
        {
            services.AddSingleton<IEmailSender, ConsoleEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, UnconfiguredEmailSender>();
        }

        return services;
    }
}
