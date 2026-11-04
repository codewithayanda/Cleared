using Cleared.Domain.Common;
using Microsoft.Extensions.Options;

namespace Cleared.Infrastructure.Email;

public sealed class SmtpOptionsValidator : IValidateOptions<SmtpOptions>
{
    private static readonly TimeSpan _shortestTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _longestTimeout = TimeSpan.FromMinutes(2);

    public ValidateOptionsResult Validate(string? name, SmtpOptions options)
    {
        var problems = new List<string>();
        var hasUsername = !string.IsNullOrWhiteSpace(options.Username);
        var hasPassword = !string.IsNullOrEmpty(options.Password);

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            problems.Add("Email:Smtp:Host is empty.");
        }

        if (options.Port is < 1 or > 65535)
        {
            problems.Add("Email:Smtp:Port must be between 1 and 65535.");
        }

        if (!Enum.IsDefined(options.TlsMode))
        {
            problems.Add("Email:Smtp:TlsMode must be None, StartTls or SslOnConnect.");
        }

        if (!EmailAddressRule.IsValid(options.From))
        {
            problems.Add("Email:Smtp:From must be an email address, for example no-reply@yourcompany.example.");
        }

        if (hasUsername != hasPassword)
        {
            problems.Add("Email:Smtp:Username and Email:Smtp:Password go together: set both or neither.");
        }
        else if (hasUsername && options.TlsMode == SmtpTlsMode.None)
        {
            problems.Add("Email:Smtp:TlsMode must be StartTls or SslOnConnect when a username is set, so the password is never sent in the clear.");
        }

        if (options.Timeout < _shortestTimeout || options.Timeout > _longestTimeout)
        {
            problems.Add("Email:Smtp:Timeout must be between 1 second and 2 minutes.");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
