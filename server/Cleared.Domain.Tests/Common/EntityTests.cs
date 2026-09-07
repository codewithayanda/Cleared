using Cleared.Domain.Common;

namespace Cleared.Domain.Tests.Common;

public class EntityTests
{
    private sealed class TestEntity : Entity
    {
        public TestEntity(Guid id) : base(id) { }
    }

    private sealed class OtherEntity : Entity
    {
        public OtherEntity(Guid id) : base(id) { }
    }

    [Fact]
    public void Constructor_EmptyGuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => new TestEntity(Guid.Empty));
    }

    [Fact]
    public void Equals_SameTypeAndId_AreEqual()
    {
        var id = Guid.NewGuid();

        Assert.Equal(new TestEntity(id), new TestEntity(id));
    }

    [Fact]
    public void Equals_SameIdDifferentType_AreNotEqual()
    {
        var id = Guid.NewGuid();
        Entity a = new TestEntity(id);
        Entity b = new OtherEntity(id);

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equals_DifferentId_AreNotEqual()
    {
        Assert.NotEqual(new TestEntity(Guid.NewGuid()), new TestEntity(Guid.NewGuid()));
    }
}
