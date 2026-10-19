using Cleared.Domain.Common;

namespace Cleared.Domain.Tenancy;

public sealed class Customer : Entity
{
    public Guid TenantId { get; }
    public string Name { get; private set; }
    public string? VatNumber { get; private set; }
    public string? Email { get; private set; }

    // Required on a tax invoice's recipient details (VAT Act s20(4)); see InvoiceService's
    // tax-invoice validation. Free text, not a structured value object: it is only ever
    // printed, never parsed or geocoded.
    public string? Address { get; private set; }

    // Private setter, not a constructor parameter. See InvoiceLineItem.UnitPrice.
    public DateTimeOffset CreatedAt { get; private set; }

    private Customer(
        Guid id, Guid tenantId, string name, string? vatNumber, string? email, string? address)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        VatNumber = vatNumber;
        Email = email;
        Address = address;
    }

    public static Customer Create(
        Guid id,
        Guid tenantId,
        string name,
        DateTimeOffset createdAt,
        string? vatNumber = null,
        string? email = null,
        string? address = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A customer must belong to a tenant.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A customer must have a name.", nameof(name));
        }

        RequireValidEmail(email);

        return new Customer(id, tenantId, name, vatNumber, email, address)
        {
            CreatedAt = createdAt,
        };
    }

    public void UpdateDetails(string name, string? vatNumber = null, string? email = null, string? address = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A customer must have a name.", nameof(name));
        }

        RequireValidEmail(email);

        Name = name;
        VatNumber = vatNumber;
        Email = email;
        Address = address;
    }

    // No email is fine. A blank one is not: absent means null.
    private static void RequireValidEmail(string? email)
    {
        if (email is not null && !EmailAddressRule.IsValid(email))
        {
            throw new ArgumentException("A customer's email must be a valid email address.", nameof(email));
        }
    }
}
