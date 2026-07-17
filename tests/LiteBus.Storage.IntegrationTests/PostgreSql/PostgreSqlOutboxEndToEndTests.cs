using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

public sealed class PostgreSqlOutboxEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlOutboxEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishEventThroughPostgreSqlStore()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var recorder = new EventRecorder();

         var provider = BuildProvider(_fixture, options, recorder);
         await using (provider.ConfigureAwait(false))
         {
        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.WithIdentity(
            new OrderSubmittedIntegrationEvent { OrderId = orderId },
            messageId)).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        recorder.Events.Should().ContainSingle(@event => @event.OrderId == orderId);

        var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId).ConfigureAwait(false);
        row!.Status.Should().Be(OutboxStatus.Published);
        row.AttemptCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenDispatcherFails_ShouldMarkFailedWithVisibleAfter()
    {
        var clock = new ManualTimeProvider(PostgreSqlTestInfrastructure.BaseTime);
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);

         var provider = BuildProvider(             _fixture,             options,             null,             clock,             true,             5,             TimeSpan.FromMinutes(2));
         await using (provider.ConfigureAwait(true))
         {

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.WithIdentity(
            new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            messageId)).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId).ConfigureAwait(false);
        row!.Status.Should().Be(OutboxStatus.Failed);
        row.VisibleAfter.Should().Be(PostgreSqlTestInfrastructure.BaseTime.AddMinutes(2));
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenMaxAttemptsExceeded_ShouldMoveToDeadLetter()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);

         var provider = BuildProvider(             _fixture,             options,             null,             useFailingDispatcher: true,             maxAttempts: 1);
         await using (provider.ConfigureAwait(true))
         {

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.WithIdentity(
            new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            messageId)).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId).ConfigureAwait(false);
        row!.Status.Should().Be(OutboxStatus.DeadLettered);
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenLeaseExpires_ShouldReclaimAndPublishMessage()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var recorder = new EventRecorder();

        var provider = BuildProvider(_fixture, options, recorder);
        await using (provider.ConfigureAwait(false))
        {
            var outbox = provider.GetRequiredService<IOutbox>();
            var processor = provider.GetRequiredService<IOutboxProcessor>();
            var leaseStore = provider.GetRequiredService<IOutboxLeaseStore>();
            var messageId = Guid.NewGuid();
            var orderId = Guid.NewGuid();

            await outbox.EnqueueAsync(OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.WithIdentity(
                new OrderSubmittedIntegrationEvent { OrderId = orderId },
                messageId)).ConfigureAwait(false);

            await leaseStore.LeasePendingAsync(new OutboxLeaseRequest
            {
                BatchSize = 1,
                LeaseOwner = "stale-publisher",
                Now = PostgreSqlTestInfrastructure.BaseTime,
                LeaseDuration = TimeSpan.FromMinutes(1)
            }).ConfigureAwait(false);

            await PostgreSqlDatabaseTimeTestSupport.ExpireLeaseAsync(
                _fixture.DataSource,
                options.SchemaName,
                options.TableName,
                messageId).ConfigureAwait(false);
            await processor.ProcessPendingAsync().ConfigureAwait(false);

            recorder.Events.Should().ContainSingle(@event => @event.OrderId == orderId);

            var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId).ConfigureAwait(false);
            row!.Status.Should().Be(OutboxStatus.Published);
            row.AttemptCount.Should().Be(2);
        }
    }

    [Fact]
    public async Task AddAsync_WithVisibleAfter_ShouldDeferPublishingUntilDue()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var recorder = new EventRecorder();
        var visibleAfter = DateTimeOffset.UtcNow.AddHours(1);

        var provider = BuildProvider(_fixture, options, recorder);
        await using (provider.ConfigureAwait(false))
        {
            var outbox = provider.GetRequiredService<IOutbox>();
            var processor = provider.GetRequiredService<IOutboxProcessor>();
            var messageId = Guid.NewGuid();

            await outbox.EnqueueAsync(OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.From(
                new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
                OutboxEnqueueMetadata.Immediate with
                {
                    Identity = new MessageIdentity.Supplied(messageId),
                    Visibility = new MessageVisibility.At(visibleAfter)
                })).ConfigureAwait(false);

            await processor.ProcessPendingAsync().ConfigureAwait(false);
            recorder.Events.Should().BeEmpty();

            await PostgreSqlDatabaseTimeTestSupport.MakeVisibleAsync(
                _fixture.DataSource,
                options.SchemaName,
                options.TableName,
                messageId).ConfigureAwait(false);
            await processor.ProcessPendingAsync().ConfigureAwait(false);

            recorder.Events.Should().ContainSingle();
        }
    }

    private static ServiceProvider BuildProvider(
        PostgreSqlFixture fixture,
        PostgreSqlOutboxStoreOptions options,
        EventRecorder? recorder,
        TimeProvider? clock = null,
        bool useFailingDispatcher = false,
        int maxAttempts = 5,
        TimeSpan? initialDelay = null)
    {
        var services = new ServiceCollection();

        if (recorder is not null)
        {
            services.AddSingleton(recorder);
        }

        if (useFailingDispatcher)
        {
            services.AddSingleton<IOutboxDispatcher, AlwaysFailingOutboxDispatcher>();
        }

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddEventModule(builder =>
            {
                builder.Register<OrderSubmittedEventHandler>();
            });

            registry.AddOutboxModule(builder =>
            {
                builder.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(fixture.DataSource);
                    postgres.UseOptions(options);
                });

                builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted");

                builder.UseProcessorOptions(new OutboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "pg-e2e-publisher",
                    Retry = new RetryOptions
                    {
                        MaxAttempts = maxAttempts,
                        InitialDelay = initialDelay ?? TimeSpan.Zero,
                        UseJitter = false
                    }
                });

                if (!useFailingDispatcher)
                {
                    builder.UseInProcessDispatch();
                }
            });
        });

        if (clock is not null)
        {
            services.AddSingleton<TimeProvider>(clock);
        }

        return services.BuildServiceProvider();
    }
}
