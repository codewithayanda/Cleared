namespace Cleared.Application.Payments;

// ReceivedAt is client-supplied, not defaulted to today: the Owner is recording money
// that already arrived, often days ago, by the time they get to entering it.
public sealed record RecordPaymentRequest(string Amount, DateOnly ReceivedAt, string? Reference);

public sealed record PaymentResponse(
    Guid Id, Guid TenantId, Guid InvoiceId, string Method, string Amount, DateOnly ReceivedAt, string? Reference);
