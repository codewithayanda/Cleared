namespace Cleared.Application.Idempotency;

public sealed class IdempotencyKeyReusedException()
    : Exception("This Idempotency-Key was already used for a different request.");
