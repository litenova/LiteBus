using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.Storage.InMemory.UnitTests;

public sealed class InMemoryOutboxStorageModuleTests
{
    [Fact]
    public void AddInMemoryOutboxStorage_ShouldRegisterAllStoreRoles()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(configuration => configuration.AddInMemoryOutboxStorage())
            .BuildServiceProvider();

        provider.GetRequiredService<IOutboxStore>().Should().BeOfType<InMemoryOutboxStore>();
        provider.GetRequiredService<IOutboxLeaseStore>().Should().BeSameAs(provider.GetRequiredService<IOutboxStore>());
        provider.GetRequiredService<IOutboxStateStore>().Should().BeSameAs(provider.GetRequiredService<IOutboxStore>());
        provider.GetRequiredService<InMemoryOutboxStore>().Should().BeSameAs(provider.GetRequiredService<IOutboxStore>());
    }
}
