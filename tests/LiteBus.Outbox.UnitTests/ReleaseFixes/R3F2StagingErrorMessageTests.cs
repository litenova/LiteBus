using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies the collected-events staging failure names Entity Framework Core and its non-generic adapter.
/// </summary>
public sealed class R3F2StagingErrorMessageTests
{
    /// <summary>
    ///     Verifies the missing outbox error mentions EF Core so EF users know which adapter to register.
    /// </summary>
    [Fact]
    public async Task PublishAsync_with_collected_events_but_no_outbox_should_name_ef_core()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder => builder.Register<CollectingHandler>());
            registry.AddOutbox(builder =>
            {
                builder.Contracts.Register<StagedEvent>("tests.events.staged");
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
            });
        });
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new Trigger { OrderId = Guid.NewGuid() });

        var failure = await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
        failure.Which.Message.Should().Contain("StageCollectedEvents");
        failure.Which.Message.Should().Contain("Entity Framework Core");
        failure.Which.Message.Should().Contain("ITransactionalOutbox");
    }

    private sealed record Trigger
    {
        public Guid OrderId { get; init; }
    }

    private sealed record StagedEvent
    {
        public Guid OrderId { get; init; }
    }

    private sealed class CollectingHandler : IEventHandler<Trigger>
    {
        private readonly IDomainEventCollector _collector;

        public CollectingHandler(IDomainEventCollector collector)
        {
            ArgumentNullException.ThrowIfNull(collector);

            _collector = collector;
        }

        public Task HandleAsync(Trigger message, CancellationToken cancellationToken = default)
        {
            _collector.Collect(new StagedEvent { OrderId = message.OrderId });
            return Task.CompletedTask;
        }
    }
}
