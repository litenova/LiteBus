using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies builder-registered tenant routing and contract resolvers reach the dispatcher and envelope factory.
/// </summary>
public sealed class OutboxTenantRoutingRegistrationTests
{
    /// <summary>
    ///     Verifies a strategy set on <see cref="TransportOutboxDispatcherOptions.TenantRoutingStrategy" /> is consulted by the resolved dispatcher.
    /// </summary>
    [Fact]
    public async Task Dispatcher_WhenTenantRoutingRegistered_ShouldConsultStrategy()
    {
        var transport = new TestMessageTransport();
        var strategy = new RecordingRoutingStrategy();

        using var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(outbox =>
                {
                    outbox.Contracts.Register<StoredEvent>("orders.events.stored");
                    outbox.UseInMemoryStorage();
                    outbox.RegisterDispatcher(new ForwardingDispatchModule(transport, strategy));
                });
            })
            .BuildServiceProvider();

        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();

        await dispatcher.DispatchAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "orders.events.stored",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Publishing,
            AttemptCount = 1,
            TenantId = "tenant-east",
            Topic = "orders.priority"
        }).ConfigureAwait(false);

        strategy.Consulted.Should().BeTrue();
        strategy.TenantId.Should().Be("tenant-east");
        strategy.ContractName.Should().Be("orders.events.stored");
        strategy.Topic.Should().Be("orders.priority");
        transport.Published.Should().ContainSingle().Which.Route.Should().Be("tenant-east.orders.priority");
    }

    /// <summary>
    ///     Verifies a resolver registered through <c>UseContractResolver</c> resolves from the container.
    /// </summary>
    [Fact]
    public void ContractResolver_WhenRegistered_ShouldResolveRegisteredInstance()
    {
        var resolver = new DeclaredTypeMessageContractResolver();

        using var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(outbox =>
                {
                    outbox.Contracts.Register<StoredEvent>("orders.events.stored");
                    outbox.UseInMemoryStorage();
                    outbox.UseContractResolver(resolver);
                    outbox.UseRecordingOutboxDispatcher(new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder());
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<IMessageContractResolver>().Should().BeSameAs(resolver);
    }

    /// <summary>
    ///     Sample event used by registration tests.
    /// </summary>
    private sealed record StoredEvent
    {
        /// <summary>
        ///     Gets the order identifier carried by the event payload.
        /// </summary>
        public string OrderId { get; init; } = string.Empty;
    }

    /// <summary>
    ///     Records tenant routing inputs and returns a deterministic route.
    /// </summary>
    private sealed class RecordingRoutingStrategy : ITenantRoutingStrategy
    {
        /// <summary>
        ///     Gets a value indicating whether the dispatcher consulted this strategy.
        /// </summary>
        public bool Consulted { get; private set; }

        /// <summary>
        ///     Gets the tenant identifier passed by the dispatcher.
        /// </summary>
        public string? TenantId { get; private set; }

        /// <summary>
        ///     Gets the stable contract name passed by the dispatcher.
        /// </summary>
        public string? ContractName { get; private set; }

        /// <summary>
        ///     Gets the topic hint passed by the dispatcher.
        /// </summary>
        public string? Topic { get; private set; }

        /// <inheritdoc />
        public string ResolveRoute(string? tenantId, string contractName, string? topic)
        {
            Consulted = true;
            TenantId = tenantId;
            ContractName = contractName;
            Topic = topic;
            return $"{tenantId}.{topic}";
        }
    }

    /// <summary>
    ///     Registers the transport outbox dispatcher against a test transport.
    /// </summary>
    private sealed class ForwardingDispatchModule : IOutboxDispatcherModule
    {
        /// <summary>
        ///     The test transport that receives published envelopes.
        /// </summary>
        private readonly TestMessageTransport _transport;

        /// <summary>
        ///     The tenant routing strategy set on the dispatcher options.
        /// </summary>
        private readonly ITenantRoutingStrategy _strategy;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ForwardingDispatchModule" /> class.
        /// </summary>
        /// <param name="transport">The test transport that receives published envelopes.</param>
        /// <param name="strategy">The tenant routing strategy set on the dispatcher options.</param>
        public ForwardingDispatchModule(TestMessageTransport transport, ITenantRoutingStrategy strategy)
        {
            _transport = transport;
            _strategy = strategy;
        }

        /// <inheritdoc />
        public void Build(IModuleConfiguration configuration)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(ITransportPublisher),
                _transport));

            new TransportOutboxDispatchModule<ForwardingDispatchModule>(new TransportOutboxDispatcherOptions
            {
                DefaultDestination = "orders.events",
                TenantRoutingStrategy = _strategy
            }).Build(configuration);
        }
    }
}
