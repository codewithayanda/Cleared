using Cleared.Domain.Common;

namespace Cleared.Domain.Tenancy;

public sealed class Tenant : Entity
{
    public string CompanyName { get; private set; }
    public string? TradingName { get; private set; }
    public string? VatNumber { get; private set; }
    public VatStatus VatStatus { get; private set; }

    // Not a constructor parameter — see the note on InvoiceLineItem.UnitPrice for the
    // general shape of this issue. DateTimeOffset specifically was rejected by EF Core's
    // constructor-binding here in a way Guid/string/enum parameters were not; moving it to
    // a private setter set post-construction is the same proven workaround.
    public DateTimeOffset CreatedAt { get; private set; }

    private Tenant(
        Guid id,
        string companyName,
        string? tradingName,
        VatStatus vatStatus,
        string? vatNumber)
        : base(id)
    {
        CompanyName = companyName;
        TradingName = tradingName;
        VatStatus = vatStatus;
        VatNumber = vatNumber;
    }

    public static Tenant Register(
        Guid id,
        string companyName,
        VatStatus vatStatus,
        string? vatNumber,
        DateTimeOffset createdAt,
        string? tradingName = null)
    {
        if (string.IsNullOrWhiteSpace(companyName))
        {
            throw new ArgumentException("A tenant must have a company name.", nameof(companyName));
        }

        if (vatStatus == VatStatus.Registered && string.IsNullOrWhiteSpace(vatNumber))
        {
            throw new ArgumentException(
                "A VAT-registered tenant must have a VAT number.", nameof(vatNumber));
        }

        if (vatStatus == VatStatus.NotRegistered && !string.IsNullOrWhiteSpace(vatNumber))
        {
            throw new ArgumentException(
                "A tenant that is not VAT registered cannot have a VAT number.", nameof(vatNumber));
        }

        return new Tenant(id, companyName, tradingName, vatStatus, vatNumber)
        {
            CreatedAt = createdAt,
        };
    }

    public bool CanIssueTaxInvoices() => VatStatus == VatStatus.Registered;

    public void RegisterForVat(string vatNumber)
    {
        if (string.IsNullOrWhiteSpace(vatNumber))
        {
            throw new ArgumentException(
                "A VAT number is required to register for VAT.", nameof(vatNumber));
        }

        if (VatStatus == VatStatus.Registered)
        {
            throw new InvalidOperationException("This tenant is already VAT registered.");
        }

        VatStatus = VatStatus.Registered;
        VatNumber = vatNumber;
    }

    public void Rename(string companyName, string? tradingName = null)
    {
        if (string.IsNullOrWhiteSpace(companyName))
        {
            throw new ArgumentException("A tenant must have a company name.", nameof(companyName));
        }

        CompanyName = companyName;
        TradingName = tradingName;
    }
}
