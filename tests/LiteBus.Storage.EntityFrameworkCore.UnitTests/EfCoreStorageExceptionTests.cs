using LiteBus.Storage.EntityFrameworkCore.Exceptions;

namespace LiteBus.Storage.EntityFrameworkCore.UnitTests;

public sealed class EfCoreStorageExceptionTests
{
    [Fact]
    public void EfCoreProviderResolver_throws_EfCoreStorageNotSupportedException_for_unknown_provider()
    {
        var act = () => EfCoreProviderResolver.ResolveProviderName("Unknown.Provider");

        act.Should().Throw<EfCoreStorageNotSupportedException>()
            .WithMessage("*Unknown.Provider*");
    }
}