using Cleared.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cleared.Infrastructure.Email;

public static class EmailServiceCollectionExtensions
{
    // Mail leaves after the response, one message at a time, from a bounded queue. Only development
    // prints it. Everywhere else drops it, with a warning, until a real sender is set up.
    public static IServiceCollection AddEmail(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddSingleton<EmailQueue>();
        services.AddSingleton<IEmailQueue>(provider => provider.GetRequiredService<EmailQueue>());
        services.AddHostedService<EmailDispatcher>();

        if (environment.IsDevelopment())
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
