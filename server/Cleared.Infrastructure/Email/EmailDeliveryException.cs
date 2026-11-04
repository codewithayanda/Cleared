namespace Cleared.Infrastructure.Email;

// Says what went wrong with a delivery and nothing else: never to whom, about what, or with which password.
public sealed class EmailDeliveryException(string message) : Exception(message);
