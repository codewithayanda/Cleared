using System.Globalization;
using Cleared.Application.Customers;
using Cleared.Application.Invoices;
using Cleared.Application.Payments;
using Cleared.Application.Tenants;

namespace Cleared.Application.Tests.Invoices;

// What a client reads on an invoice PDF is decided here, so each state of an invoice gets its own test.
public class InvoiceDocumentBuilderTests
{
    private static readonly DateOnly _today = new(2026, 11, 4);

    private static readonly CustomerResponse _customer =
        new(Guid.NewGuid(), Guid.NewGuid(), "Beta Co", null, null, "1 Main Road");

    private static TenantResponse Tenant(bool bankDetails = true) => new(
        Guid.NewGuid(), "Acme Ltd", null, "Registered", "4123456789", "1 Acme Way",
        bankDetails ? "FNB" : null, bankDetails ? "62123456789" : null, bankDetails ? "250655" : null);

    // R705.00 with VAT at 15%, which is 613.04 and 91.96.
    private static InvoiceResponse Invoice(
        string status, string documentType = "TaxInvoice", string paid = "0.00", string balance = "705.00",
        decimal quantity = 1.5m, string rate = "0.15") => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), status, documentType, "INV-2026-0007",
        new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 9), new DateOnly(2026, 11, 8), rate,
        "613.04", "91.96", "705.00", paid, balance, false,
        [new InvoiceLineResponse(Guid.NewGuid(), "Consulting", quantity, "408.69", "613.04", "Standard", "91.96", "705.00")]);

    private static PaymentResponse Payment(string amount, DateOnly receivedAt, int recordedMinute = 0) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Manual", amount, receivedAt,
        new DateTimeOffset(2026, 10, 20, 9, recordedMinute, 0, TimeSpan.Zero), null);

    private static InvoiceDocument Build(
        InvoiceResponse invoice, IReadOnlyList<PaymentResponse>? payments = null, TenantResponse? tenant = null) =>
        InvoiceDocumentBuilder.Build(invoice, _customer, tenant ?? Tenant(), payments ?? [], _today);

    private static T WithCulture<T>(string name, Func<T> action)
    {
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(name);

            return action();
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
        }
    }

    [Fact]
    public void Build_NothingPaid_SaysTotalDueWithTheFullTotalAndShowsHowToPay()
    {
        var content = Build(Invoice("Issued"));

        Assert.Equal("Total due", content.TotalLabel);
        Assert.Equal("705.00", content.TotalAmount);
        Assert.Null(content.Mark);
        Assert.Null(content.InvoiceTotal);
        Assert.Null(content.PaymentsHeading);
        Assert.Empty(content.Payments);
        Assert.True(content.ShowBanking);
    }

    [Fact]
    public void Build_PartPaid_SaysBalanceDueWithWhatIsLeftAndListsThePayments()
    {
        var content = Build(
            Invoice("PartiallyPaid", paid: "400.00", balance: "305.00"),
            [Payment("400.00", new DateOnly(2026, 10, 20))]);

        var payment = Assert.Single(content.Payments);
        Assert.Equal("Balance due", content.TotalLabel);
        Assert.Equal("305.00", content.TotalAmount);
        Assert.Equal("705.00", content.InvoiceTotal);
        Assert.Equal("Payments received as at 4 Nov 2026", content.PaymentsHeading);
        Assert.Equal(new InvoiceDocumentPayment("20 Oct 2026", "400.00"), payment);
        Assert.Null(content.Mark);
        Assert.True(content.ShowBanking);
    }

    [Fact]
    public void Build_Paid_MarksItPaidSaysPaidInFullWithNoAmountAndHidesHowToPay()
    {
        var content = Build(
            Invoice("Paid", paid: "705.00", balance: "0.00"),
            [Payment("705.00", new DateOnly(2026, 10, 20))]);

        Assert.Equal("PAID", content.Mark);
        Assert.Equal("Paid in full", content.TotalLabel);
        Assert.Null(content.TotalAmount);
        Assert.Equal("705.00", content.InvoiceTotal);
        Assert.Single(content.Payments);
        Assert.False(content.ShowBanking);
    }

    [Fact]
    public void Build_Cancelled_MarksItCancelledAndHidesHowToPay()
    {
        var content = Build(Invoice("Cancelled"));

        Assert.Equal("CANCELLED", content.Mark);
        Assert.Equal("Cancelled", content.TotalLabel);
        Assert.Null(content.TotalAmount);
        Assert.Equal("705.00", content.InvoiceTotal);
        Assert.False(content.ShowBanking);
    }

    [Fact]
    public void Build_Draft_SaysTotalDueAndHasNoNumberDatesOrRate()
    {
        var draft = Invoice("Draft") with
        {
            DocumentType = null, Number = null, IssueDate = null, SupplyDate = null, DueDate = null, VatRateApplied = null,
        };

        var content = Build(draft);

        Assert.Equal("INVOICE", content.Title);
        Assert.Equal("Total due", content.TotalLabel);
        Assert.Null(content.Number);
        Assert.Null(content.IssueDate);
        Assert.Null(content.DueDate);
        Assert.Null(content.VatRate);
        Assert.Null(content.Mark);
    }

    [Fact]
    public void Build_PaymentsGivenOutOfOrder_AreListedOldestFirst()
    {
        var content = Build(
            Invoice("PartiallyPaid", paid: "500.00", balance: "205.00"),
            [
                Payment("300.00", new DateOnly(2026, 10, 25)),
                Payment("200.00", new DateOnly(2026, 10, 12)),
            ]);

        Assert.Equal(["12 Oct 2026", "25 Oct 2026"], content.Payments.Select(payment => payment.Date));
        Assert.Equal(["200.00", "300.00"], content.Payments.Select(payment => payment.Amount));
    }

    [Fact]
    public void Build_NoBankDetailsOnFile_ShowsNoBankingBlock()
    {
        var content = Build(Invoice("Issued"), tenant: Tenant(bankDetails: false));

        Assert.False(content.ShowBanking);
    }

    [Fact]
    public void Build_VatRegisteredBusiness_ShowsTheVatColumnsRowAndRate()
    {
        var content = Build(Invoice("Issued"));

        Assert.True(content.ShowVat);
        Assert.Equal("TAX INVOICE", content.Title);
        Assert.Equal("15%", content.VatRate);
    }

    [Fact]
    public void Build_BusinessNotRegisteredForVat_ShowsNoVatEvenWhenARateWasRecorded()
    {
        var content = Build(Invoice("Issued", documentType: "Invoice", rate: "0.15"));

        Assert.False(content.ShowVat);
        Assert.Equal("INVOICE", content.Title);
        Assert.Null(content.VatRate);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("en-ZA")]
    [InlineData("fr-FR")]
    public void Build_DatesAreFixedWhateverCultureTheServerRunsIn(string culture)
    {
        var content = WithCulture(
            culture,
            () => Build(
                Invoice("PartiallyPaid", paid: "400.00", balance: "305.00"),
                [Payment("400.00", new DateOnly(2026, 10, 20))]));

        Assert.Equal("9 Oct 2026", content.IssueDate);
        Assert.Equal("8 Nov 2026", content.DueDate);
        Assert.Equal("20 Oct 2026", content.Payments[0].Date);
        Assert.Equal("Payments received as at 4 Nov 2026", content.PaymentsHeading);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("en-ZA")]
    [InlineData("fr-FR")]
    public void Build_QuantityAndVatRateAreFixedWhateverCultureTheServerRunsIn(string culture)
    {
        var content = WithCulture(culture, () => Build(Invoice("Issued", quantity: 1.5m, rate: "0.15")));

        Assert.Equal("1.5", content.Lines[0].Quantity);
        Assert.Equal("15%", content.VatRate);
    }
}
