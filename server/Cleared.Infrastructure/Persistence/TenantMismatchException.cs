namespace Cleared.Infrastructure.Persistence;

// Deliberately not an ArgumentException or InvalidOperationException: DomainExceptionHandler
// maps those to 400 and 409, and a tenant mismatch is a bug in our code, so it must be a 500.
public sealed class TenantMismatchException : Exception
{
    public TenantMismatchException(string message)
        : base(message)
    {
    }

    public TenantMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
