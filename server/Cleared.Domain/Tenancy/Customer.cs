using Cleared.Domain.Common;

namespace Cleared.Domain.Tenancy;

public sealed class Customer : Entity
{
    public Guid TenantId { get; }
    public string Name { get; private set; }
    public string? VatNumber { get; private set; }
    public string? Email { get; private set; }

    // Not a constructor parameter — see the note on Tenant.CreatedAt / InvoiceLineItem.UnitPrice.
    public DateTimeOffset CreatedAt { get; private set; }

    private Customer(
        Guid id, Guid tenantId, string name, string? vatNumber, string? email)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        VatNumber = vatNumber;
        Email = email;
    }

    public static Customer Create(
        Guid id,
        Guid tenantId,
        string name,
        DateTimeOffset createdAt,
        string? vatNumber = null,
        string? email = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A customer must belong to a tenant.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A customer must have a name.", nameof(name));
        }

        return new Customer(id, tenantId, name, vatNumber, email)
        {
            CreatedAt = createdAt,
        };
    }

    public void UpdateDetails(string name, string? vatNumber = null, string? email = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A customer must have a name.", nameof(name));
        }

        Name = name;
        VatNumber = vatNumber;
        Email = email;
    }
}
