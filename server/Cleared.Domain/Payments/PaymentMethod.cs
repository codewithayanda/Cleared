namespace Cleared.Domain.Payments;

// Only Manual is reachable today; there is no gateway integration yet. Adding one means a
// new Payment.RecordFromGateway() factory rather than a new enum value.
public enum PaymentMethod
{
    Manual,
    Eft,
    Card,
}
