using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.Storage.InMemory.UnitTests;

public sealed class InMemoryOutboxStorageModuleTests
{
    [Fact]
    public void UseInMemoryStorage_ShouldRegisterAllStoreRoles()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(modules => modules.AddOutboxModule(outbox => outbox.UseInMemoryStorage()))
            .BuildServiceProvider();

        provider.GetRequiredService<IOutboxStore>().Should().BeOfType<InMemoryOutboxStore>();
        provider.GetRequiredService<IOutboxLeaseStore>().Should().BeSameAs(provider.GetRequiredService<IOutboxStore>());
        provider.GetRequiredService<IOutboxStateWriter>().Should().BeSameAs(provider.GetRequiredService<IOutboxStore>());
        provider.GetRequiredService<IOutboxDeadLetterStore>().Should().BeSameAs(provider.GetRequiredService<IOutboxStore>());
        provider.GetRequiredService<IOutboxRetentionStore>().Should().BeSameAs(provider.GetRequiredService<IOutboxStore>());
        provider.GetRequiredService<IOutboxDiagnosticsStore>().Should().BeSameAs(provider.GetRequiredService<IOutboxStore>());
        provider.GetRequiredService<InMemoryOutboxStore>().Should().BeSameAs(provider.GetRequiredService<IOutboxStore>());
    }
}
