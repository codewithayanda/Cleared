namespace Cleared.Application.Abstractions;

// A forgotten password is reset through a link sent to the email. Asking for the link answers the same
// for an email with an account and one without, so it never shows which emails have one.
public interface IPasswordResetService
{
    Task<ResetRequestOutcome> RequestAsync(string email, CancellationToken cancellationToken);

    // Never signs anyone in. The link works once, and a password that breaks the rules leaves it usable.
    Task<ResetOutcome> ResetAsync(string token, string newPassword, CancellationToken cancellationToken);
}

// RetryAfter is set when this email has asked too often. Nothing was done, and it says how long to wait.
public sealed record ResetRequestOutcome(TimeSpan? RetryAfter);

public enum ResetResult
{
    Succeeded,
    InvalidLink,
    WeakPassword,
}

public sealed record ResetOutcome(ResetResult Result, IReadOnlyList<string> PasswordProblems)
{
    public static ResetOutcome Succeeded() => new(ResetResult.Succeeded, []);

    public static ResetOutcome InvalidLink() => new(ResetResult.InvalidLink, []);

    public static ResetOutcome WeakPassword(IReadOnlyList<string> problems) => new(ResetResult.WeakPassword, problems);
}
