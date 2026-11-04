namespace Cleared.Application.Auth;

// Thrown when something is allowed only once the account's email has been confirmed.
public sealed class EmailNotVerifiedException(string message) : Exception(message);
