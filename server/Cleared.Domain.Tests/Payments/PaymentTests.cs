using Cleared.Domain.Common;
using Cleared.Domain.Payments;

namespace Cleared.Domain.Tests.Payments;

public class PaymentTests
{
    private static readonly Guid _tenantId = Guid.NewGuid();
    private static readonly Guid _invoiceId = Guid.NewGuid();
    private static readonly DateOnly _receivedAt = new(2026, 9, 10);

    [Fact]
    public void RecordManual_ZeroAmount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Payment.RecordManual(Guid.NewGuid(), _tenantId, _invoiceId, Money.Zero, _receivedAt));
    }

    [Fact]
    public void RecordManual_NegativeAmount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Payment.RecordManual(Guid.NewGuid(), _tenantId, _invoiceId, Money.Zar(-1m), _receivedAt));
    }

    [Fact]
    public void RecordManual_ValidInput_SetsAllFields()
    {
        var payment = Payment.RecordManual(
            Guid.NewGuid(), _tenantId, _invoiceId, Money.Zar(500m), _receivedAt, "EFT ref 384930");

        Assert.Equal(_tenantId, payment.TenantId);
        Assert.Equal(_invoiceId, payment.InvoiceId);
        Assert.Equal(PaymentMethod.Manual, payment.Method);
        Assert.Equal(Money.Zar(500m), payment.Amount);
        Assert.Equal(_receivedAt, payment.ReceivedAt);
        Assert.Equal("EFT ref 384930", payment.Reference);
    }

    [Fact]
    public void RecordManual_NoReference_LeavesReferenceNull()
    {
        var payment = Payment.RecordManual(Guid.NewGuid(), _tenantId, _invoiceId, Money.Zar(500m), _receivedAt);

        Assert.Null(payment.Reference);
    }
}
