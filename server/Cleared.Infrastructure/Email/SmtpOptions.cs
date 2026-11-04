namespace Cleared.Infrastructure.Email;

public enum SmtpTlsMode
{
    None,
    StartTls,
    SslOnConnect,
}

// A class and not a record, so nothing that prints the options can print the password.
public sealed class SmtpOptions
{
    public const string SectionName = "Email:Smtp";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    // StartTls refuses a server that cannot encrypt, so nothing is sent in the clear by accident.
    public SmtpTlsMode TlsMode { get; set; } = SmtpTlsMode.StartTls;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;

    public string FromName { get; set; } = "Cleared";

    // How long to wait for the server at each step before giving up.
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
}
