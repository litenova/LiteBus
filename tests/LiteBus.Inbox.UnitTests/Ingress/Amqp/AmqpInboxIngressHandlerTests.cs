using System.Text;
using System.Text.Json;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Abstractions.Exceptions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Amqp;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.UnitTests.Ingress.Amqp;

[Collection("Sequential")]
public sealed class AmqpInboxIngressHandlerTests : LiteBusTestBase
{
    [Fact]
    public async Task AcceptAsync_ShouldDeserializeAndWriteToInboxWithMappedHeaders()
    {
        var messageId = Guid.NewGuid();
        var visibleAfter = DateTimeOffset.UtcNow.AddMinutes(-1);
        var command = new ShipOrderCommand { OrderId = Guid.NewGuid() };

        var provider = BuildProvider();
        await using (provider.ConfigureAwait(false))
        {
            var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();

            await handler.AcceptAsync(CreateMessage(
                command,
                headers =>
                {
                    headers[TransportHeaders.MessageId] = messageId.ToString();
                    headers["litebus-idempotency-key"] = "idem-42";
                    headers[TransportHeaders.CorrelationId] = "correlation-1";
                    headers[TransportHeaders.CausationId] = "causation-2";
                    headers[TransportHeaders.TenantId] = "tenant-west";
                    headers["litebus-visible-after"] = visibleAfter.ToString("O");
                },
                "property-correlation")).ConfigureAwait(false);


            var leaseStore = provider.GetRequiredService<IInboxLeaseStore>();

            var leased = await leaseStore.LeasePendingAsync(new InboxLeaseRequest
            {
                BatchSize = 10,
                LeaseOwner = "ingress-unit-test",
                Now = DateTimeOffset.UtcNow,
                LeaseDuration = TimeSpan.FromMinutes(1)
            });

            leased.Should().ContainSingle();
            leased[0].Id.Should().Be(messageId);
            leased[0].IdempotencyKey.Should().Be("ingress:unknown:" + messageId.ToString("D"));
            leased[0].CorrelationId.Should().Be("correlation-1");
            leased[0].CausationId.Should().Be("causation-2");
            leased[0].TenantId.Should().BeNull();
            leased[0].VisibleAfter.Should().Be(visibleAfter);
        }
    }

    [Fact]
    public async Task AcceptAsync_WhenContractHeaderMissing_ShouldThrow()
    {
        var provider = BuildProvider();
        await using (provider.ConfigureAwait(false))
        {
            var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();

            var message = CreateMessage(
                new ShipOrderCommand { OrderId = Guid.NewGuid() },
                headers => headers.Remove(TransportHeaders.ContractName));

            var act = () => handler.AcceptAsync(message);

            await act.Should().ThrowAsync<InboxIngressException>()
                .WithMessage("*litebus-contract-name*required*");
        }
    }

    [Fact]
    public async Task AcceptAsync_WhenContractVersionIsInvalid_ShouldThrow()
    {
        var provider = BuildProvider();
        await using (provider.ConfigureAwait(false))
        {
            var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();

            var act = () => handler.AcceptAsync(CreateMessage(
                new ShipOrderCommand { OrderId = Guid.NewGuid() },
                headers => headers[TransportHeaders.ContractVersion] = "not-a-number"));

            await act.Should().ThrowAsync<InboxIngressException>()
                .WithMessage("*positive integer*");
        }
    }

    [Fact]
    public async Task AcceptAsync_WhenContractVersionIsZero_ShouldThrow()
    {
        var provider = BuildProvider();
        await using (provider.ConfigureAwait(false))
        {
            var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();

            var act = () => handler.AcceptAsync(CreateMessage(
                new ShipOrderCommand { OrderId = Guid.NewGuid() },
                headers => headers[TransportHeaders.ContractVersion] = "0"));

            await act.Should().ThrowAsync<InboxIngressException>()
                .WithMessage("*positive integer*");
        }
    }

    [Fact]
    public async Task AcceptAsync_WhenMessageIsNull_ShouldThrow()
    {
        var provider = BuildProvider();
        await using (provider.ConfigureAwait(false))
        {
            var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();

            var act = () => handler.AcceptAsync(null!);

            await act.Should().ThrowAsync<ArgumentNullException>();
        }
    }

    [Fact]
    public async Task AcceptAsync_ShouldConvertByteArrayAndMemoryHeaders()
    {
        var provider = BuildProvider();
        await using (provider.ConfigureAwait(false))
        {
            var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();
            var command = new ShipOrderCommand { OrderId = Guid.NewGuid() };

            await handler.AcceptAsync(CreateMessage(
                command,
                headers =>
                {
                    headers[TransportHeaders.CorrelationId] = Encoding.UTF8.GetBytes("bytes-correlation");
                    headers[TransportHeaders.TenantId] = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("memory-tenant"));
                    headers[TransportHeaders.CausationId] = new Memory<byte>(Encoding.UTF8.GetBytes("memory-causation"));
                })).ConfigureAwait(false);


            var leaseStore = provider.GetRequiredService<IInboxLeaseStore>();

            var leased = await leaseStore.LeasePendingAsync(new InboxLeaseRequest
            {
                BatchSize = 10,
                LeaseOwner = "ingress-unit-test",
                Now = DateTimeOffset.UtcNow,
                LeaseDuration = TimeSpan.FromMinutes(1)
            });

            leased.Should().ContainSingle();
            leased[0].CorrelationId.Should().Be("bytes-correlation");
            leased[0].TenantId.Should().BeNull();
            leased[0].CausationId.Should().Be("memory-causation");
        }
    }

    [Fact]
    public async Task AcceptAsync_WhenMessageIdHeaderInvalid_ShouldLeaveInboxIdUnset()
    {
        var provider = BuildProvider();
        await using (provider.ConfigureAwait(false))
        {
            var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();

            await handler.AcceptAsync(CreateMessage(
                new ShipOrderCommand { OrderId = Guid.NewGuid() },
                headers => headers[TransportHeaders.MessageId] = "not-a-guid")).ConfigureAwait(false);


            var leaseStore = provider.GetRequiredService<IInboxLeaseStore>();

            var leased = await leaseStore.LeasePendingAsync(new InboxLeaseRequest
            {
                BatchSize = 10,
                LeaseOwner = "ingress-unit-test",
                Now = DateTimeOffset.UtcNow,
                LeaseDuration = TimeSpan.FromMinutes(1)
            });

            leased.Should().ContainSingle();
            leased[0].Id.Should().NotBe(Guid.Empty);
        }
    }

    [Fact]
    public async Task AcceptAsync_WhenVisibleAfterHeaderInvalid_ShouldIgnoreVisibleAfter()
    {
        var provider = BuildProvider();
        await using (provider.ConfigureAwait(false))
        {
            var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();

            await handler.AcceptAsync(CreateMessage(
                new ShipOrderCommand { OrderId = Guid.NewGuid() },
                headers => headers["litebus-visible-after"] = "not-a-date")).ConfigureAwait(false);


            var leaseStore = provider.GetRequiredService<IInboxLeaseStore>();

            var leased = await leaseStore.LeasePendingAsync(new InboxLeaseRequest
            {
                BatchSize = 10,
                LeaseOwner = "ingress-unit-test",
                Now = DateTimeOffset.UtcNow,
                LeaseDuration = TimeSpan.FromMinutes(1)
            });

            leased.Should().ContainSingle();
            leased[0].VisibleAfter.Should().BeNull();
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

                registry.AddCommands(module => module.Register<ShipOrderCommandHandler>());

                registry.AddInbox(inbox =>
                {
                    inbox.Contracts.Register<ShipOrderCommand>("orders.commands.ship");
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

    private static AmqpReceivedMessage CreateMessage(
        ShipOrderCommand command,
        Action<Dictionary<string, object?>>? configureHeaders = null,
        string? correlationId = null)
    {
        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [TransportHeaders.ContractName] = "orders.commands.ship",
            [TransportHeaders.ContractVersion] = "1",
            [TransportHeaders.MessageId] = Guid.NewGuid().ToString("D")
        };

        configureHeaders?.Invoke(headers);

        return new AmqpReceivedMessage
        {
            Body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command)),
            Headers = headers,
            DeliveryTag = 1,
            CorrelationId = correlationId,
            AckDelegate = (_, _) => Task.CompletedTask,
            NackDelegate = (_, _, _) => Task.CompletedTask
        };
    }

    public sealed record ShipOrderCommand : ICommand
    {
        public Guid OrderId { get; init; }
    }

    public sealed class ShipOrderCommandHandler : ICommandHandler<ShipOrderCommand>
    {
        public Task HandleAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
