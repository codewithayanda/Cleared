using Cleared.Domain.Common;

namespace Cleared.Domain.Tenancy;

public sealed class Tenant : Entity
{
    public string CompanyName { get; private set; }
    public string? TradingName { get; private set; }
    public string? VatNumber { get; private set; }
    public VatStatus VatStatus { get; private set; }

    // Required on a tax invoice's supplier details (VAT Act s20(4)), same as
    // Customer.Address. Nullable because it is not known at sign-up; enforced at Issue().
    public string? Address { get; private set; }

    // Free text, not structured fields: South African EFT needs only these three to be
    // identifiable to a paying customer, and nothing validates them against a real bank.
    // Not required to register or issue, unlike Address. Without them, EFT just isn't usable.
    public string? BankName { get; private set; }
    public string? BankAccountNumber { get; private set; }
    public string? BankBranchCode { get; private set; }

    // Private setter, not a constructor parameter: EF Core's constructor binding rejects
    // DateTimeOffset here, where Guid, string and enum parameters bind fine.
    // See InvoiceLineItem.UnitPrice.
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
        string? tradingName = null,
        string? address = null,
        string? bankName = null,
        string? bankAccountNumber = null,
        string? bankBranchCode = null)
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
            Address = address,
            BankName = bankName,
            BankAccountNumber = bankAccountNumber,
            BankBranchCode = bankBranchCode,
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

    // Separate from Rename/RegisterForVat: this is the company-profile settings screen
    // updating contact and payment details, not the identity fields those two touch.
    public void UpdateProfile(string? address, string? bankName, string? bankAccountNumber, string? bankBranchCode)
    {
        Address = address;
        BankName = bankName;
        BankAccountNumber = bankAccountNumber;
        BankBranchCode = bankBranchCode;
    }
}
