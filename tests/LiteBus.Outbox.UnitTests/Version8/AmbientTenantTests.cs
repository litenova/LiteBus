using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.Version8;

/// <summary>
///     Verifies the ambient tenant option stamps the ambient identity tenant onto enqueued envelopes whose items carry none.
/// </summary>
[Collection("Sequential")]
public sealed class AmbientTenantTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies the factory stamps the ambient tenant when opted in and the item tenant is unscoped.
    /// </summary>
    [Fact]
    public async Task CreateAsync_when_opted_in_and_item_tenant_is_unscoped_should_stamp_ambient_tenant()
    {
        var factory = CreateFactory(new OutboxEnqueueOptions { UseAmbientTenant = true });

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(ExecutionIdentity.ForTenant("tenant-ambient"))))
        {
            var envelope = await factory.CreateAsync(OutboxEnqueueItem<TenantedEvent>.From(new TenantedEvent())).ConfigureAwait(false);

            envelope.TenantId.Should().Be("tenant-ambient");
        }
    }

    /// <summary>
    ///     Verifies the factory ignores the ambient tenant when the option is off.
    /// </summary>
    [Fact]
    public async Task CreateAsync_when_not_opted_in_should_ignore_ambient_tenant()
    {
        var factory = CreateFactory(new OutboxEnqueueOptions());

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(ExecutionIdentity.ForTenant("tenant-ambient"))))
        {
            var envelope = await factory.CreateAsync(OutboxEnqueueItem<TenantedEvent>.From(new TenantedEvent())).ConfigureAwait(false);

            envelope.TenantId.Should().BeNull();
        }
    }

    /// <summary>
    ///     Verifies an explicit item tenant wins over the ambient tenant when opted in.
    /// </summary>
    [Fact]
    public async Task CreateAsync_when_item_tenant_is_explicit_should_keep_it()
    {
        var factory = CreateFactory(new OutboxEnqueueOptions { UseAmbientTenant = true });

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(ExecutionIdentity.ForTenant("tenant-ambient"))))
        {
            var item = OutboxEnqueueItem<TenantedEvent>.From(
                new TenantedEvent(),
                OutboxEnqueueMetadata.Immediate with { Tenant = new TenantScope.Isolated("tenant-explicit") });
            var envelope = await factory.CreateAsync(item).ConfigureAwait(false);

            envelope.TenantId.Should().Be("tenant-explicit");
        }
    }

    /// <summary>
    ///     Verifies an opted-in enqueue outside a mediation stays unscoped.
    /// </summary>
    [Fact]
    public async Task CreateAsync_when_opted_in_without_ambient_context_should_stay_unscoped()
    {
        var factory = CreateFactory(new OutboxEnqueueOptions { UseAmbientTenant = true });

        var envelope = await factory.CreateAsync(OutboxEnqueueItem<TenantedEvent>.From(new TenantedEvent())).ConfigureAwait(false);

        envelope.TenantId.Should().BeNull();
    }

    /// <summary>
    ///     Verifies an enqueue inside a tenant mediation stamps the ambient tenant end to end when opted in.
    /// </summary>
    [Fact]
    public async Task Enqueue_inside_a_tenant_mediation_should_stamp_ambient_tenant_when_opted_in()
    {
        var provider = BuildProvider(useAmbientTenant: true);
        var settings = new EventMediationSettings
        {
            Identity = ExecutionIdentity.ForTenant("tenant-7")
        };

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new EnqueueTrigger(), settings).ConfigureAwait(false);

        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        var envelope = store.GetAll().Should().ContainSingle().Subject;
        envelope.TenantId.Should().Be("tenant-7");
    }

    /// <summary>
    ///     Verifies an enqueue inside a tenant mediation stays unscoped end to end without the option.
    /// </summary>
    [Fact]
    public async Task Enqueue_inside_a_tenant_mediation_should_stay_unscoped_without_the_option()
    {
        var provider = BuildProvider(useAmbientTenant: false);
        var settings = new EventMediationSettings
        {
            Identity = ExecutionIdentity.ForTenant("tenant-7")
        };

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new EnqueueTrigger(), settings).ConfigureAwait(false);

        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        var envelope = store.GetAll().Should().ContainSingle().Subject;
        envelope.TenantId.Should().BeNull();
    }

    private static OutboxEnvelopeFactory CreateFactory(OutboxEnqueueOptions options)
    {
        var contracts = new MessageContractRegistry();
        contracts.Register<TenantedEvent>("tests.events.tenanted");

        return new OutboxEnvelopeFactory(
            contracts,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System,
            null,
            null,
            null,
            options);
    }

    private static ServiceProvider BuildProvider(bool useAmbientTenant)
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder => builder.Register<EnqueueingHandler>());
            registry.AddOutbox(builder =>
            {
                builder.Contracts.Register<TenantedEvent>("tests.events.tenanted");
                builder.UseInMemoryStorage();

                if (useAmbientTenant)
                {
                    builder.UseAmbientTenant();
                }
            });
        });

        return services.BuildServiceProvider();
    }

    private sealed record TenantedEvent;

    private sealed record EnqueueTrigger;

    /// <summary>
    ///     Enqueues one event through the singleton outbox inside the mediation.
    /// </summary>
    private sealed class EnqueueingHandler : IEventHandler<EnqueueTrigger>
    {
        private readonly IOutbox _outbox;

        /// <summary>
        ///     Initializes a new instance of the <see cref="EnqueueingHandler" /> class.
        /// </summary>
        /// <param name="outbox">The outbox writer.</param>
        public EnqueueingHandler(IOutbox outbox)
        {
            _outbox = outbox;
        }

        /// <inheritdoc />
        public Task HandleAsync(EnqueueTrigger message, CancellationToken cancellationToken = default)
        {
            return _outbox.EnqueueAsync(OutboxEnqueueItem<TenantedEvent>.From(new TenantedEvent()), cancellationToken);
        }
    }

    private sealed class TestExecutionContext : IExecutionContext
    {
        public TestExecutionContext(ExecutionIdentity identity)
        {
            Identity = identity;
        }

        public CancellationToken CancellationToken => CancellationToken.None;

        public DateTimeOffset HandledAt { get; } = DateTimeOffset.UnixEpoch;

        public ExecutionIdentity Identity { get; }

        public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();

        public IHandleContextData Data { get; } = new HandleContextData();

        public IReadOnlyCollection<string> Tags { get; } = [];

        public object? MessageResult { get; set; }

        public bool PostHandlersSuppressed { get; private set; }

        public void SuppressPostHandlers()
        {
            PostHandlersSuppressed = true;
        }
    }
}
