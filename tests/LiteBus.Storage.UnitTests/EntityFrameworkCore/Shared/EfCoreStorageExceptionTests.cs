using LiteBus.Storage.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.Shared;

public sealed class EfCoreStorageExceptionTests
{
    [Fact]
    public void EfCoreProviderResolver_throws_EfCoreStorageNotSupportedException_for_unknown_provider()
    {
        var act = () => EfCoreProviderResolver.ResolveProviderName("Unknown.Provider");

        act.Should().Throw<EfCoreStorageNotSupportedException>()
            .WithMessage("*Unknown.Provider*");
    }

    [Theory]
    [InlineData(EfCoreRelationalProviderNames.InMemory, EfCoreStorageProvider.InMemory)]
    [InlineData(EfCoreRelationalProviderNames.PostgreSql, EfCoreStorageProvider.PostgreSql)]
    [InlineData(EfCoreRelationalProviderNames.SqlServer, EfCoreStorageProvider.SqlServer)]
    [InlineData(EfCoreRelationalProviderNames.MySql, EfCoreStorageProvider.MySql)]
    [InlineData(EfCoreRelationalProviderNames.Sqlite, EfCoreStorageProvider.Sqlite)]
    public void ResolveProviderName_ShouldMapSupportedProviders(
        string providerName,
        EfCoreStorageProvider expected)
    {
        EfCoreProviderResolver.ResolveProviderName(providerName).Should().Be(expected);
    }

    [Theory]
    [InlineData(EfCoreStorageProvider.SqlServer, "dbo")]
    [InlineData(EfCoreStorageProvider.PostgreSql, "public")]
    [InlineData(EfCoreStorageProvider.MySql, "")]
    [InlineData(EfCoreStorageProvider.Sqlite, "")]
    [InlineData(EfCoreStorageProvider.InMemory, "dbo")]
    public void GetRecommendedDefaultSchema_ShouldReturnProviderDefault(
        EfCoreStorageProvider provider,
        string expected)
    {
        EfCoreProviderResolver.GetRecommendedDefaultSchema(provider).Should().Be(expected);
    }

    [Fact]
    public void Resolve_WithExplicitOverride_ShouldSkipProviderInference()
    {
        var options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new DbContext(options);

        var provider = EfCoreProviderResolver.Resolve(context, EfCoreStorageProvider.PostgreSql);

        provider.Should().Be(EfCoreStorageProvider.PostgreSql);
    }
}
