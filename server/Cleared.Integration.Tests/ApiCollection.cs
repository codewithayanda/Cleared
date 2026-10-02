namespace Cleared.Integration.Tests;

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ClearedApiFactory>
{
    public const string Name = "api";
}
