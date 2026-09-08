using Cleared.Domain.Auditing;

namespace Cleared.Domain.Tests.Auditing;

public class AuditLogTests
{
    private static readonly Guid _tenantId = Guid.NewGuid();
    private static readonly Guid _userId = Guid.NewGuid();
    private static readonly Guid _entityId = Guid.NewGuid();
    private static readonly DateTimeOffset _occurredAt = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Record_BlankEntityType_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AuditLog.Record(Guid.NewGuid(), _tenantId, _userId, " ", _entityId, "Issued", _occurredAt));
    }

    [Fact]
    public void Record_BlankAction_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AuditLog.Record(Guid.NewGuid(), _tenantId, _userId, "Invoice", _entityId, " ", _occurredAt));
    }

    [Fact]
    public void Record_ValidInput_SetsAllFields()
    {
        var entry = AuditLog.Record(
            Guid.NewGuid(), _tenantId, _userId, "Invoice", _entityId, "Issued", _occurredAt,
            "Issued as INV-2026-0001, total R1150.00");

        Assert.Equal(_tenantId, entry.TenantId);
        Assert.Equal(_userId, entry.UserId);
        Assert.Equal("Invoice", entry.EntityType);
        Assert.Equal(_entityId, entry.EntityId);
        Assert.Equal("Issued", entry.Action);
        Assert.Equal(_occurredAt, entry.OccurredAt);
        Assert.Equal("Issued as INV-2026-0001, total R1150.00", entry.Details);
    }

    [Fact]
    public void Record_NoDetails_LeavesDetailsNull()
    {
        var entry = AuditLog.Record(Guid.NewGuid(), _tenantId, _userId, "Invoice", _entityId, "Issued", _occurredAt);

        Assert.Null(entry.Details);
    }
}
