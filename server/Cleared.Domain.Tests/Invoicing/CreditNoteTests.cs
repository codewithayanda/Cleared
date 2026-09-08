using Cleared.Domain.Common;
using Cleared.Domain.Invoicing;

namespace Cleared.Domain.Tests.Invoicing;

public class CreditNoteTests
{
    private static readonly Guid _tenantId = Guid.NewGuid();
    private static readonly Guid _invoiceId = Guid.NewGuid();
    private static readonly DateOnly _issueDate = new(2026, 9, 1);
    private const decimal StandardRate = 0.15m;

    private static CreditNoteLineRequest Line(
        decimal quantity, decimal unitPrice, VatTreatment vatTreatment = VatTreatment.Standard) =>
        new(Guid.NewGuid(), "Item", quantity, Money.Zar(unitPrice), vatTreatment);

    [Fact]
    public void Create_BlankNumber_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreditNote.Create(
            Guid.NewGuid(), _tenantId, _invoiceId, " ", "Pricing error", _issueDate, StandardRate, [Line(1m, 100m)]));
    }

    [Fact]
    public void Create_BlankReason_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreditNote.Create(
            Guid.NewGuid(), _tenantId, _invoiceId, "CN-2026-0001", " ", _issueDate, StandardRate, [Line(1m, 100m)]));
    }

    [Fact]
    public void Create_NoLines_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreditNote.Create(
            Guid.NewGuid(), _tenantId, _invoiceId, "CN-2026-0001", "Pricing error", _issueDate, StandardRate, []));
    }

    [Fact]
    public void Create_ValidInput_SetsHeaderFields()
    {
        var creditNote = CreditNote.Create(
            Guid.NewGuid(), _tenantId, _invoiceId, "CN-2026-0001", "Pricing error", _issueDate, StandardRate,
            [Line(1m, 100m)]);

        Assert.Equal(_tenantId, creditNote.TenantId);
        Assert.Equal(_invoiceId, creditNote.InvoiceId);
        Assert.Equal("CN-2026-0001", creditNote.Number);
        Assert.Equal("Pricing error", creditNote.Reason);
        Assert.Equal(_issueDate, creditNote.IssueDate);
        Assert.Single(creditNote.Lines);
    }

    [Fact]
    public void Create_StandardLine_ChargesVatAtTheGivenRate()
    {
        var creditNote = CreditNote.Create(
            Guid.NewGuid(), _tenantId, _invoiceId, "CN-2026-0001", "Pricing error", _issueDate, StandardRate,
            [Line(2m, 500m, VatTreatment.Standard)]);

        // 2 * 500 = 1000 subtotal, VAT at 15% = 150.
        Assert.Equal(Money.Zar(1000m), creditNote.Subtotal);
        Assert.Equal(Money.Zar(150m), creditNote.VatTotal);
        Assert.Equal(Money.Zar(1150m), creditNote.Total);
    }

    [Fact]
    public void Create_ZeroRatedLine_ChargesNoVat()
    {
        var creditNote = CreditNote.Create(
            Guid.NewGuid(), _tenantId, _invoiceId, "CN-2026-0001", "Return", _issueDate, StandardRate,
            [Line(1m, 500m, VatTreatment.ZeroRated)]);

        Assert.Equal(Money.Zero, creditNote.VatTotal);
        Assert.Equal(Money.Zar(500m), creditNote.Total);
    }

    [Fact]
    public void Create_UsesTheSuppliedRate_NotAHardcodedOne()
    {
        // Simulates crediting an invoice that was issued under an OLD rate, after the
        // rate has since changed — the credit note must use the original invoice's rate,
        // never "today's" rate (see CreditNoteLineItem.Create).
        const decimal historicalRate = 0.14m;

        var creditNote = CreditNote.Create(
            Guid.NewGuid(), _tenantId, _invoiceId, "CN-2026-0001", "Return", _issueDate, historicalRate,
            [Line(1m, 1000m, VatTreatment.Standard)]);

        Assert.Equal(Money.Zar(140m), creditNote.VatTotal);
    }

    [Fact]
    public void Create_MultipleLines_AggregatesAcrossAllOfThem()
    {
        var creditNote = CreditNote.Create(
            Guid.NewGuid(), _tenantId, _invoiceId, "CN-2026-0001", "Partial return", _issueDate, StandardRate,
            [Line(1m, 1000m, VatTreatment.Standard), Line(1m, 500m, VatTreatment.ZeroRated)]);

        Assert.Equal(Money.Zar(1500m), creditNote.Subtotal);
        Assert.Equal(Money.Zar(150m), creditNote.VatTotal);
        Assert.Equal(Money.Zar(1650m), creditNote.Total);
    }
}
