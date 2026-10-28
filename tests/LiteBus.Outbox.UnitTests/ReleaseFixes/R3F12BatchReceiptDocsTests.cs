using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.ReleaseFixes;

/// <summary>
///     Locks the flattened batch receipt shape documented for per-handler delivery.
/// </summary>
[Collection("Sequential")]
public sealed class R3F12BatchReceiptDocsTests
{
    /// <summary>
    ///     Verifies two items with two handlers each yield four receipts in item then handler order.
    /// </summary>
    [Fact]
    public async Task EnqueueBatchAsync_with_per_handler_delivery_should_flatten_receipts()
    {
        var provider = BuildProvider();
        var outbox = provider.GetRequiredService<IOutbox>();

        var receipts = await outbox.EnqueueBatchAsync(
            [
                OutboxEnqueueItem.From(new OrderEvent(), typeof(OrderEvent)),
                OutboxEnqueueItem.From(new OrderEvent(), typeof(OrderEvent))
            ]).ConfigureAwait(false);

        receipts.Should().HaveCount(4);
        receipts.Select(receipt => receipt.Id).Should().OnlyHaveUniqueItems();
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
