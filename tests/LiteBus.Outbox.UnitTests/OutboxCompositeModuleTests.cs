using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests;

public sealed class OutboxCompositeModuleTests
{
    [Fact]
    public void InMemoryStorageModule_WhenRegisteredWithoutOutboxCore_ShouldThrow()
    {
        var act = () =>
        {
            new ServiceCollection()
                .AddLiteBus(modules => modules.Register(new InMemoryOutboxStorageModule()))
                .BuildServiceProvider();
        };

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*OutboxModule*UseInMemoryStorage*");
    }

    [Fact]
    public void AddOutboxModule_WithNestedStorageAndDispatcher_ShouldResolveOutboxServices()
    {
        var services = new ServiceCollection()
            .AddLiteBus(modules =>
            {
                modules.AddOutboxModule(outbox =>
                {
                    outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>(
                        "orders.events.submitted",
                        1);
                    outbox.UseInMemoryStorage();
                });
            });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOutbox>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxStore>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxLeaseStore>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxTerminalStateStore>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxRetentionStore>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxDiagnosticsStore>().Should().NotBeNull();
    }
}
