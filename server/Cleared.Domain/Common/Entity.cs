namespace Cleared.Domain.Common;

public abstract class Entity : IEquatable<Entity>
{
public Guid Id { get; }

protected Entity(Guid id)
{
    if (id == Guid.Empty)
    {
        throw new ArgumentException("An entity id cannot be empty.", nameof(id));
    }

    Id = id;
}

public bool Equals(Entity? other) =>
    other is not null && other.GetType() == GetType() && Id == other.Id;

public override bool Equals(object? obj) => Equals(obj as Entity);

public override int GetHashCode() => HashCode.Combine(GetType(), Id);

public static bool operator ==(Entity? left, Entity? right) =>
    left is null ? right is null : left.Equals(right);

public static bool operator !=(Entity? left, Entity? right) => !(left == right);
}
