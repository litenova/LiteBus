using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     End-to-end subscriber name tests with Entity Framework Core storage and PostgreSQL.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class EfCoreOutboxSubscriberNameEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string ContractName = "orders.events.subscriber-name";
    private const string BillingSubscriberName = "billing.order-created";
    private const string ShippingSubscriberName = "shipping.order-created";
    private const string SubscriberTableName = "outbox_ef_subscriber_names";
    private const string LegacyTableName = "outbox_ef_subscriber_legacy";

    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxSubscriberNameEndToEndTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreOutboxSubscriberNameEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies that per-handler delivery writes the subscriber name to stored outbox rows and derives the per-handler idempotency keys from it.
    /// </summary>
    /// <returns>A task that completes when both subscribed handlers have run.</returns>
    [SkippableFact]
    public async Task EnqueueAsync_ShouldWriteSubscriberNamesToOutboxRows()
    {
        var storeOptions = EfCoreOutboxE2eSupport.CreateStoreOptions(SubscriberTableName);
        await EfCoreOutboxE2eSupport.EnsureOutboxTableAsync(_fixture.ConnectionString, storeOptions).ConfigureAwait(false);

        var recorder = new SubscriberDeliveryRecorder();

        var provider = BuildProvider<SubscriberNameOutboxDbContext>(_fixture.ConnectionString, storeOptions, recorder, "efcore-outbox-subscriber-names");
        await using (provider.ConfigureAwait(true))
        {

            var outbox = provider.GetRequiredService<IOutbox>();
            var processor = provider.GetRequiredService<IOutboxProcessor>();

            var orderId = Guid.NewGuid();

            await outbox.EnqueueAsync(OutboxEnqueueItem<SubscriberOrderEvent>.WithIdempotency(
                new SubscriberOrderEvent { OrderId = orderId },
                "order-1")).ConfigureAwait(false);

            var rows = await ReadRowsAsync<SubscriberNameOutboxDbContext>(provider).ConfigureAwait(false);

            rows.Should().HaveCount(2);
            rows.Select(row => row.Handler).Should().BeEquivalentTo(BillingSubscriberName, ShippingSubscriberName);
            rows.Select(row => row.IdempotencyKey).Should().BeEquivalentTo(
                $"order-1:{BillingSubscriberName}",
                $"order-1:{ShippingSubscriberName}");

            await processor.ProcessPendingAsync().ConfigureAwait(false);

            recorder.Deliveries.Select(delivery => delivery.SubscriberName).Should().BeEquivalentTo(BillingSubscriberName, ShippingSubscriberName);
            recorder.Deliveries.Should().OnlyContain(delivery => delivery.OrderId == orderId);

            var published = await ReadRowsAsync<SubscriberNameOutboxDbContext>(provider).ConfigureAwait(false);

            published.Should().HaveCount(2);
            published.Should().OnlyContain(row => row.Status == OutboxStatus.Published);
        }
    }

    /// <summary>
    ///     Verifies that a row addressed by handler full type name is still delivered after the handler adopts a subscriber name.
    /// </summary>
    /// <returns>A task that completes when the legacy row is delivered.</returns>
    [SkippableFact]
    public async Task ProcessPendingAsync_ShouldDeliverRowAddressedByFullTypeName()
    {
        var storeOptions = EfCoreOutboxE2eSupport.CreateStoreOptions(LegacyTableName);
        await EfCoreOutboxE2eSupport.EnsureOutboxTableAsync(_fixture.ConnectionString, storeOptions).ConfigureAwait(false);

        var recorder = new SubscriberDeliveryRecorder();

        var provider = BuildProvider<LegacySubscriberOutboxDbContext>(_fixture.ConnectionString, storeOptions, recorder, "efcore-outbox-subscriber-legacy");
        await using (provider.ConfigureAwait(true))
        {

            var serializer = provider.GetRequiredService<IMessageSerializer>();
            var store = provider.GetRequiredService<IOutboxStore>();
            var processor = provider.GetRequiredService<IOutboxProcessor>();

            var orderId = Guid.NewGuid();
            var payload = await serializer.SerializeAsync(new SubscriberOrderEvent { OrderId = orderId }).ConfigureAwait(false);

            var legacyId = Guid.NewGuid();

            await store.AddAsync(new OutboxEnvelope
            {
                Id = legacyId,
                ContractName = ContractName,
                ContractVersion = 1,
                Payload = payload,
                Handler = typeof(BillingReaction).FullName,
                CreatedAt = DateTimeOffset.UtcNow,
                Status = OutboxStatus.Pending,
                AttemptCount = 0
            }).ConfigureAwait(false);

            await processor.ProcessPendingAsync().ConfigureAwait(false);

            recorder.Deliveries.Should().ContainSingle();
            recorder.Deliveries[0].SubscriberName.Should().Be(BillingSubscriberName);
            recorder.Deliveries[0].OrderId.Should().Be(orderId);

            var rows = await ReadRowsAsync<LegacySubscriberOutboxDbContext>(provider).ConfigureAwait(false);

            rows.Should().ContainSingle(row => row.Id == legacyId && row.Status == OutboxStatus.Published);
        }
    }

    private static ServiceProvider BuildProvider<TDbContext>(
        string connectionString,
        EntityFrameworkCoreOutboxStoreOptions storeOptions,
        SubscriberDeliveryRecorder recorder,
        string leaseOwner)
        where TDbContext : EfCoreOutboxE2eDbContext
    {
        var services = new ServiceCollection();

        services.AddSingleton(recorder);
        services.AddSingleton(storeOptions);
        services.AddDbContextFactory<TDbContext>(options =>
            options.UseNpgsql(EfCoreProviderTestInfrastructure.CreateOutboxScopedConnectionString(connectionString, storeOptions)));

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(module =>
            {
                module.Register<BillingReaction>();
                module.Register<ShippingReaction>();
            });

            registry.AddOutbox(outbox =>
            {
                outbox.UseEntityFrameworkCoreStorage(builder =>
                {
                    builder.UseDbContext<TDbContext>();
                    builder.UseOptions(storeOptions);
                });

                outbox.Contracts.Register<SubscriberOrderEvent>(ContractName);

                outbox.UseProcessorOptions(new OutboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = leaseOwner,
                    Retry = new RetryOptions
                    {
                        MaxAttempts = 5,
                        InitialDelay = TimeSpan.Zero,
                        UseJitter = false
                    }
                });

                outbox.UseInProcessDispatch();
                outbox.EnablePerHandlerDelivery();
            });
        });

        return services.BuildServiceProvider();
    }

    private static async Task<IReadOnlyList<OutboxMessageEntity>> ReadRowsAsync<TDbContext>(ServiceProvider provider)
        where TDbContext : EfCoreOutboxE2eDbContext
    {
        var factory = provider.GetRequiredService<IDbContextFactory<TDbContext>>();
        var context = await factory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        await using var configuredContext = context.ConfigureAwait(false);

        return await context.OutboxMessages.ToListAsync().ConfigureAwait(false);
    }

    private sealed class SubscriberNameOutboxDbContext : EfCoreOutboxE2eDbContext
    {
        public SubscriberNameOutboxDbContext(DbContextOptions<SubscriberNameOutboxDbContext> options, EntityFrameworkCoreOutboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }

    private sealed class LegacySubscriberOutboxDbContext : EfCoreOutboxE2eDbContext
    {
        public LegacySubscriberOutboxDbContext(DbContextOptions<LegacySubscriberOutboxDbContext> options, EntityFrameworkCoreOutboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }

    private sealed record SubscriberOrderEvent
    {
        public Guid OrderId { get; init; }
    }

    private sealed record SubscriberDelivery(string SubscriberName, Guid OrderId);

    private sealed class SubscriberDeliveryRecorder
    {
        private readonly object _gate = new();
        private readonly List<SubscriberDelivery> _deliveries = [];

        public IReadOnlyList<SubscriberDelivery> Deliveries
        {
            get
            {
                lock (_gate)
                {
                    return [.. _deliveries];
                }
            }
        }

        public void Record(string subscriberName, Guid orderId)
        {
            lock (_gate)
            {
                _deliveries.Add(new SubscriberDelivery(subscriberName, orderId));
            }
        }
    }

    [SubscriberName(BillingSubscriberName)]
    private sealed class BillingReaction : IEventHandler<SubscriberOrderEvent>
    {
        private readonly SubscriberDeliveryRecorder _recorder;

        public BillingReaction(SubscriberDeliveryRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(SubscriberOrderEvent message, CancellationToken cancellationToken = default)
        {
            _recorder.Record(BillingSubscriberName, message.OrderId);
            return Task.CompletedTask;
        }
    }

    [SubscriberName(ShippingSubscriberName)]
    private sealed class ShippingReaction : IEventHandler<SubscriberOrderEvent>
    {
        private readonly SubscriberDeliveryRecorder _recorder;

        public ShippingReaction(SubscriberDeliveryRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(SubscriberOrderEvent message, CancellationToken cancellationToken = default)
        {
            _recorder.Record(ShippingSubscriberName, message.OrderId);
            return Task.CompletedTask;
        }
    }
}
