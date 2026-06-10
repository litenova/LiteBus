using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Storage.InMemory.UnitTests;

public sealed class InMemoryInboxStorageModuleTests
{
    [Fact]
    public void UseInMemoryStorage_ShouldRegisterAllStoreRoles()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ => { });
                registry.AddInboxModule(inbox => inbox.UseInMemoryStorage());
            })
            .BuildServiceProvider();

        provider.GetRequiredService<IInboxStore>().Should().BeOfType<InMemoryInboxStore>();
        provider.GetRequiredService<IInboxLeaseStore>().Should().BeSameAs(provider.GetRequiredService<IInboxStore>());
        provider.GetRequiredService<IInboxStateWriter>().Should().BeSameAs(provider.GetRequiredService<IInboxStore>());
        provider.GetRequiredService<IInboxDeadLetterStore>().Should().BeSameAs(provider.GetRequiredService<IInboxStore>());
        provider.GetRequiredService<IInboxRetentionStore>().Should().BeSameAs(provider.GetRequiredService<IInboxStore>());
        provider.GetRequiredService<IInboxDiagnosticsStore>().Should().BeSameAs(provider.GetRequiredService<IInboxStore>());
        provider.GetRequiredService<InMemoryInboxStore>().Should().BeSameAs(provider.GetRequiredService<IInboxStore>());
    }

    [Fact]
    public void UseInMemoryStorage_WithCustomTimeProvider_ShouldRegisterTimeProvider()
    {
        var timeProvider = TimeProvider.System;

        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ => { });
                registry.AddInboxModule(inbox =>
                    inbox.UseInMemoryStorage(builder => builder.UseTimeProvider(timeProvider)));
            })
            .BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(timeProvider);
    }

    [Fact]
    public void UseInMemoryStorage_WhenCalledTwiceOnBuilder_ShouldThrow()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ => { });
                registry.AddInboxModule(inbox =>
                {
                    inbox.UseInMemoryStorage();
                    inbox.UseInMemoryStorage();
                });
            })
            .BuildServiceProvider();

        act.Should().Throw<LiteBus.Runtime.Abstractions.Exceptions.LiteBusConfigurationException>()
            .WithMessage("*Inbox storage is already configured*");
    }
}
