using LiteBus.Storage.EntityFrameworkCore.Exceptions;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.Shared;

public sealed class EfCoreProviderRoutingTests
{
    [Fact]
    public void ResolveProviderName_WithInMemorySubstring_ShouldThrowNotSupported()
    {
        var act = () => EfCoreProviderResolver.ResolveProviderName("Contoso.EntityFrameworkCore.InMemory.Extended");

        act.Should().Throw<EfCoreStorageNotSupportedException>();
    }

    [Fact]
    public void ResolveProviderName_WithSqliteSubstring_ShouldThrowNotSupported()
    {
        var act = () => EfCoreProviderResolver.ResolveProviderName("Contoso.EntityFrameworkCore.Sqlite.Extended");

        act.Should().Throw<EfCoreStorageNotSupportedException>();
    }

    [Fact]
    public void ResolveProviderName_WithDifferentCasing_ShouldThrowNotSupported()
    {
        var act = () => EfCoreProviderResolver.ResolveProviderName("microsoft.entityframeworkcore.inmemory");

        act.Should().Throw<EfCoreStorageNotSupportedException>();
    }
}
