using System.Text;
using System.Text.Json;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Abstractions.Exceptions;
using LiteBus.Inbox.Dispatch.InMemory;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Amqp;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Ingress.Amqp.UnitTests;

[Collection("Sequential")]
public sealed class AmqpInboxIngressHandlerTests : LiteBusTestBase
{
    [Fact]
    public async Task AcceptAsync_ShouldDeserializeAndWriteToInboxWithMappedHeaders()
    {
        var messageId = Guid.NewGuid();
        var visibleAfter = DateTimeOffset.UtcNow.AddMinutes(-1);
        var command = new ShipOrderCommand { OrderId = Guid.NewGuid() };

        await using var provider = BuildProvider();
        var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();

        await handler.AcceptAsync(CreateMessage(
            command,
            headers =>
            {
                headers[AmqpHeaders.MessageId] = messageId.ToString();
                headers["litebus-idempotency-key"] = "idem-42";
                headers[AmqpHeaders.CorrelationId] = "correlation-1";
                headers[AmqpHeaders.CausationId] = "causation-2";
                headers[AmqpHeaders.TenantId] = "tenant-west";
                headers["litebus-visible-after"] = visibleAfter.ToString("O");
            },
            "property-correlation"));

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

    [Fact]
    public async Task AcceptAsync_WhenContractHeaderMissing_ShouldThrow()
    {
        await using var provider = BuildProvider();
        var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();

        var message = CreateMessage(
            new ShipOrderCommand { OrderId = Guid.NewGuid() },
            headers => headers.Remove(AmqpHeaders.ContractName));

        var act = () => handler.AcceptAsync(message);

        await act.Should().ThrowAsync<InboxIngressException>()
            .WithMessage("*litebus-contract-name*required*");
    }

    [Fact]
    public async Task AcceptAsync_WhenContractVersionIsInvalid_ShouldThrow()
    {
        await using var provider = BuildProvider();
        var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();

        var act = () => handler.AcceptAsync(CreateMessage(
            new ShipOrderCommand { OrderId = Guid.NewGuid() },
            headers => headers[AmqpHeaders.ContractVersion] = "not-a-number"));

        await act.Should().ThrowAsync<InboxIngressException>()
            .WithMessage("*positive integer*");
    }

    [Fact]
    public async Task AcceptAsync_WhenContractVersionIsZero_ShouldThrow()
    {
        await using var provider = BuildProvider();
        var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();

        var act = () => handler.AcceptAsync(CreateMessage(
            new ShipOrderCommand { OrderId = Guid.NewGuid() },
            headers => headers[AmqpHeaders.ContractVersion] = "0"));

        await act.Should().ThrowAsync<InboxIngressException>()
            .WithMessage("*positive integer*");
    }

    [Fact]
    public async Task AcceptAsync_WhenMessageIsNull_ShouldThrow()
    {
        await using var provider = BuildProvider();
        var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();

        var act = () => handler.AcceptAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AcceptAsync_ShouldConvertByteArrayAndMemoryHeaders()
    {
        await using var provider = BuildProvider();
        var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();
        var command = new ShipOrderCommand { OrderId = Guid.NewGuid() };

        await handler.AcceptAsync(CreateMessage(
            command,
            headers =>
            {
                headers[AmqpHeaders.CorrelationId] = Encoding.UTF8.GetBytes("bytes-correlation");
                headers[AmqpHeaders.TenantId] = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("memory-tenant"));
                headers[AmqpHeaders.CausationId] = new Memory<byte>(Encoding.UTF8.GetBytes("memory-causation"));
            }));

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

    [Fact]
    public async Task AcceptAsync_WhenMessageIdHeaderInvalid_ShouldLeaveInboxIdUnset()
    {
        await using var provider = BuildProvider();
        var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();

        await handler.AcceptAsync(CreateMessage(
            new ShipOrderCommand { OrderId = Guid.NewGuid() },
            headers => headers[AmqpHeaders.MessageId] = "not-a-guid"));

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

    [Fact]
    public async Task AcceptAsync_WhenVisibleAfterHeaderInvalid_ShouldIgnoreVisibleAfter()
    {
        await using var provider = BuildProvider();
        var handler = provider.GetRequiredService<AmqpInboxIngressHandler>();

        await handler.AcceptAsync(CreateMessage(
            new ShipOrderCommand { OrderId = Guid.NewGuid() },
            headers => headers["litebus-visible-after"] = "not-a-date"));

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

    private static ServiceProvider BuildProvider()
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddCommandModule(module => module.Register<ShipOrderCommandHandler>());

                registry.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<ShipOrderCommand>("orders.commands.ship");
                    inbox.UseInMemoryStorage();
                    inbox.UseInMemoryDispatch();

                    inbox.UseAmqpIngress(ingress =>
                    {
                        ingress.DisableIngressConsumer();

                        ingress.UseOptions(new AmqpInboxIngressOptions
                        {
                            QueueName = "litebus.inbox.ingress.unit-tests",
                            Connection = new AmqpConnectionOptions { HostName = "localhost" }
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
            [AmqpHeaders.ContractName] = "orders.commands.ship",
            [AmqpHeaders.ContractVersion] = "1",
            [AmqpHeaders.MessageId] = Guid.NewGuid().ToString("D")
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