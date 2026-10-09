using System.Text;
using System.Text.Json;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Amqp;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.UnitTests.Ingress.Amqp;

/// <summary>
///     Verifies AMQP ingress scopes broker deduplication to the consuming queue.
/// </summary>
[Collection("Sequential")]
public sealed class AmqpIngressQueueScopedDeduplicationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies deliveries from two queues bound to one exchange produce two inbox rows with queue-scoped keys.
    /// </summary>
    [Fact]
    public async Task AcceptAsync_FromTwoQueuesBoundToOneExchange_ShouldProduceTwoRowsWithQueueScopedKeys()
    {
        var firstId = Guid.NewGuid().ToString("D");
        var secondId = Guid.NewGuid().ToString("D");

        var provider = BuildProvider();
        await using (provider.ConfigureAwait(false))
        {
            var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();

            await handler.AcceptAsync(CreateMessage(firstId, "orders.fanout", "queue-a")).ConfigureAwait(false);
            await handler.AcceptAsync(CreateMessage(secondId, "orders.fanout", "queue-b")).ConfigureAwait(false);

            var leaseStore = provider.GetRequiredService<IInboxLeaseStore>();

            var leased = await leaseStore.LeasePendingAsync(new InboxLeaseRequest
            {
                BatchSize = 10,
                LeaseOwner = "ingress-unit-test",
                Now = DateTimeOffset.UtcNow,
                LeaseDuration = TimeSpan.FromMinutes(1)
            }).ConfigureAwait(false);

            leased.Should().HaveCount(2);
            leased.Should().ContainSingle(envelope => envelope.IdempotencyKey == "ingress:queue-a:" + firstId);
            leased.Should().ContainSingle(envelope => envelope.IdempotencyKey == "ingress:queue-b:" + secondId);
        }
    }

    /// <summary>
    ///     Verifies a delivery without a known queue falls back to the exchange for broker-scoped deduplication.
    /// </summary>
    [Fact]
    public async Task AcceptAsync_WithoutQueueName_ShouldFallBackToExchange()
    {
        var brokerId = Guid.NewGuid().ToString("D");

        var provider = BuildProvider();
        await using (provider.ConfigureAwait(false))
        {
            var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();

            await handler.AcceptAsync(CreateMessage(brokerId, "orders.direct", null)).ConfigureAwait(false);

            var leaseStore = provider.GetRequiredService<IInboxLeaseStore>();

            var leased = await leaseStore.LeasePendingAsync(new InboxLeaseRequest
            {
                BatchSize = 10,
                LeaseOwner = "ingress-unit-test",
                Now = DateTimeOffset.UtcNow,
                LeaseDuration = TimeSpan.FromMinutes(1)
            }).ConfigureAwait(false);

            leased.Should().ContainSingle();
            leased[0].IdempotencyKey.Should().Be("ingress:orders.direct:" + brokerId);
        }
    }

    private static ServiceProvider BuildProvider()
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                var connection = new AmqpConnectionOptions { HostName = "localhost" };
                registry.Modules.Register(new AmqpTransportModule(connection));

                registry.AddMessaging(_ =>
                {
                });

                registry.AddCommands(module => module.Register<QueueScopedOrderCommandHandler>());

                registry.AddInbox(inbox =>
                {
                    inbox.Contracts.Register<QueueScopedOrderCommand>("orders.commands.ship");
                    inbox.UseInMemoryStorage();
                    inbox.UseInProcessDispatch();

                    inbox.UseAmqpIngress(ingress =>
                    {
                        ingress.DisableIngressConsumer();

                        ingress.UseOptions(new AmqpInboxIngressOptions
                        {
                            QueueName = "litebus.inbox.ingress.unit-tests",
                        });
                    });
                });
            })
            .BuildServiceProvider();
    }

    private static AmqpReceivedMessage CreateMessage(string brokerId, string? exchange, string? queueName)
    {
        var command = new QueueScopedOrderCommand { OrderId = Guid.NewGuid() };

        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [TransportHeaders.ContractName] = "orders.commands.ship",
            [TransportHeaders.ContractVersion] = "1",
            [TransportHeaders.MessageId] = brokerId
        };

        return new AmqpReceivedMessage
        {
            Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command)),
            Headers = headers,
            DeliveryTag = 1,
            Exchange = exchange,
            QueueName = queueName,
            MessageId = brokerId,
            AckDelegate = (_, _) => Task.CompletedTask,
            NackDelegate = (_, _, _) => Task.CompletedTask
        };
    }

    public sealed record QueueScopedOrderCommand : ICommand
    {
        public Guid OrderId { get; init; }
    }

    public sealed class QueueScopedOrderCommandHandler : ICommandHandler<QueueScopedOrderCommand>
    {
        public Task HandleAsync(QueueScopedOrderCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
