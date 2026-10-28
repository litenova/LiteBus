using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies factory batch creation fans out per handler instead of returning broadcast envelopes.
/// </summary>
[Collection("Sequential")]
public sealed class R3F9BatchFanOutTests
{
    /// <summary>
    ///     Verifies one batch item with two subscribed handlers yields two addressed envelopes.
    /// </summary>
    [Fact]
    public async Task CreateBatchAsync_with_per_handler_delivery_should_fan_out()
    {
        var provider = BuildProvider();
        var factory = provider.GetRequiredService<IOutboxEnvelopeFactory>();

        var envelopes = await factory.CreateBatchAsync(
            [OutboxEnqueueItem<OrderEvent>.From(new OrderEvent())]).ConfigureAwait(false);

        envelopes.Should().HaveCount(2);
        envelopes.Should().OnlyContain(envelope => envelope.Handler != null);
        envelopes.Select(envelope => envelope.Handler).Should().OnlyHaveUniqueItems();
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder =>
            {
                builder.Register<FirstReaction>();
                builder.Register<SecondReaction>();
            });

            registry.AddOutbox(builder =>
            {
                builder.Contracts.Register<OrderEvent>("tests.events.order");
                builder.UseInMemoryStorage();
                builder.EnablePerHandlerDelivery();
            });
        });

        return services.BuildServiceProvider();
    }

    private sealed record OrderEvent;

    private sealed class FirstReaction : IEventHandler<OrderEvent>
    {
        public Task HandleAsync(OrderEvent message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SecondReaction : IEventHandler<OrderEvent>
    {
        public Task HandleAsync(OrderEvent message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
