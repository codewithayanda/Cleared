namespace Cleared.Application.Abstractions;

// An email address is proved by a link sent to it. The link is the credential, so confirming needs no
// sign-in, and what an account may do depends on the account's own flag, never on a link.
public interface IEmailVerificationService
{
    // Sends a new link, which ends the one before it. A person may be sent only so many in a while.
    Task<VerificationRequestOutcome> RequestAsync(Guid userId, CancellationToken cancellationToken);

    // True when the link was good, which it is once. Reading the page the link opens changes nothing.
    Task<bool> ConfirmAsync(string token, CancellationToken cancellationToken);

    Task<bool> IsVerifiedAsync(Guid userId, CancellationToken cancellationToken);
}

public enum VerificationRequestResult
{
    Sent,
    NothingToSend,
    TooOften,
}

// RetryAfter is set when the person has been sent too many already. Nothing was done, and it says how
// long to wait.
public sealed record VerificationRequestOutcome(VerificationRequestResult Result, TimeSpan? RetryAfter)
{
    public static VerificationRequestOutcome Sent() => new(VerificationRequestResult.Sent, null);

    public static VerificationRequestOutcome NothingToSend() => new(VerificationRequestResult.NothingToSend, null);

    public static VerificationRequestOutcome TooOften(TimeSpan wait) => new(VerificationRequestResult.TooOften, wait);
}
