namespace Cleared.Application.Abstractions;

// Limits wrong passwords per email, whether or not the email has an account, so the answer never
// shows which emails do. A try is reserved before the password is checked, so guesses that arrive
// together cannot all be checked: only the allowed number of them ever reach the password.
public interface ISignInThrottle
{
    Task<SignInTry> BeginAsync(string email, CancellationToken cancellationToken);

    // A right password wipes the count and any lock.
    Task ClearAsync(string email, CancellationToken cancellationToken);
}

// Refused: the email is locked, so the password must not be checked, and this is how long the lock
// has left. LockedIfWrong: this was the last try allowed, so a wrong password means a lock of this
// length, which has already started.
public sealed record SignInTry(TimeSpan? Refused, TimeSpan? LockedIfWrong);
