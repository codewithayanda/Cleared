namespace Cleared.Domain.Payments;

// Only Manual is reachable today — there's no gateway integration yet (deliberately
// deferred, along with every other paid service, until the functional milestones are
// done). Eft and Card are named now because they're the real, known next steps (see the
// EFT-first payment model), not speculative — adding a gateway later means a new
// Payment.RecordFromGateway()-style factory, not a new enum value.
public enum PaymentMethod
{
    Manual,
    Eft,
    Card,
}
