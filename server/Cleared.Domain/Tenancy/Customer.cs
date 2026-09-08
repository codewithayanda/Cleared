using Cleared.Domain.Common;

namespace Cleared.Domain.Tenancy;

public sealed class Customer : Entity
{
    public Guid TenantId { get; }
    public string Name { get; private set; }
    public string? VatNumber { get; private set; }
    public string? Email { get; private set; }

    // Required on a tax invoice's recipient details (VAT Act s20(4)) — see
    // InvoiceService's tax-invoice validation. Free text rather than a structured address
    // value object: nothing else in the system parses or geocodes it, it's only ever
    // printed, so a single field is enough for what this needs to do.
    public string? Address { get; private set; }

    // Not a constructor parameter — see the note on Tenant.CreatedAt / InvoiceLineItem.UnitPrice.
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

        Name = name;
        VatNumber = vatNumber;
        Email = email;
        Address = address;
    }
}
