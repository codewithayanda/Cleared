using Cleared.Domain.Common;
using Cleared.Domain.Payments;

namespace Cleared.Domain.Tests.Payments;

public class PaymentTests
{
    private static readonly Guid _tenantId = Guid.NewGuid();
    private static readonly Guid _invoiceId = Guid.NewGuid();
    private static readonly DateOnly _receivedAt = new(2026, 9, 10);
    private static readonly DateTimeOffset _recordedAt = new(2026, 9, 13, 14, 32, 7, TimeSpan.Zero);

    [Fact]
    public void RecordManual_ZeroAmount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Payment.RecordManual(Guid.NewGuid(), _tenantId, _invoiceId, Money.Zero, _receivedAt, _recordedAt));
    }

    [Fact]
    public void RecordManual_NegativeAmount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Payment.RecordManual(Guid.NewGuid(), _tenantId, _invoiceId, Money.Zar(-1m), _receivedAt, _recordedAt));
    }

    [Fact]
    public void RecordManual_ValidInput_SetsAllFields()
    {
        var payment = Payment.RecordManual(
            Guid.NewGuid(), _tenantId, _invoiceId, Money.Zar(500m), _receivedAt, _recordedAt, "EFT ref 384930");

        Assert.Equal(_tenantId, payment.TenantId);
        Assert.Equal(_invoiceId, payment.InvoiceId);
        Assert.Equal(PaymentMethod.Manual, payment.Method);
        Assert.Equal(Money.Zar(500m), payment.Amount);
        Assert.Equal(_receivedAt, payment.ReceivedAt);
        Assert.Equal(_recordedAt, payment.RecordedAt);
        Assert.Equal("EFT ref 384930", payment.Reference);
    }

    [Fact]
    public void RecordManual_NoReference_LeavesReferenceNull()
    {
        var payment = Payment.RecordManual(
            Guid.NewGuid(), _tenantId, _invoiceId, Money.Zar(500m), _receivedAt, _recordedAt);

        Assert.Null(payment.Reference);
    }

    [Fact]
    public void RecordManual_ReceivedAtCanDifferFromRecordedAt_KeepsThemDistinct()
    {
        // The date money arrived and the moment it was typed into Cleared are different
        // facts — a payment entered days late off a bank statement is the normal case,
        // not an edge case. Neither field should ever be derived from the other.
        var payment = Payment.RecordManual(
            Guid.NewGuid(), _tenantId, _invoiceId, Money.Zar(500m), _receivedAt, _recordedAt);

        Assert.NotEqual(payment.ReceivedAt, DateOnly.FromDateTime(payment.RecordedAt.Date));
    }
}
