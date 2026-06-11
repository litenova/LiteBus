using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions.Exceptions;
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
                .AddLiteBus(registry => registry.Register(new InMemoryOutboxStorageModule(_ =>
                {
                })))
                .BuildServiceProvider();
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*requires*OutboxModule*not registered*");
    }

    [Fact]
    public void AddOutboxModule_WithNestedStorageAndDispatcher_ShouldResolveOutboxServices()
    {
        var services = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddOutboxModule(outbox =>
                {
                    outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>(
                        "orders.events.submitted");

                    outbox.UseInMemoryStorage();
                });
            });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOutbox>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxStore>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxLeaseStore>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxStateWriter>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxDeadLetterStore>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxRetentionStore>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxDiagnosticsStore>().Should().NotBeNull();
    }
}