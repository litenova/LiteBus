using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.Events;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

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
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options);
        var recorder = new EventRecorder();

        await using var provider = BuildProvider(_fixture, options, recorder);
        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OrderSubmittedIntegrationEvent { OrderId = orderId }, new OutboxOptions
        {
            Id = messageId
        });

        await processor.ProcessPendingAsync();

        recorder.Events.Should().ContainSingle(@event => @event.OrderId == orderId);

        var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId);
        row!.Status.Should().Be(OutboxStatus.Published);
        row.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenDispatcherFails_ShouldMarkFailedWithVisibleAfter()
    {
        var clock = new PostgreSqlTestInfrastructure.ManualTimeProvider(PostgreSqlTestInfrastructure.BaseTime);
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options);

        await using var provider = BuildProvider(
            _fixture,
            options,
            recorder: null,
            clock: clock,
            useFailingDispatcher: true,
            maxAttempts: 5,
            initialDelay: TimeSpan.FromMinutes(2));

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
        {
            Id = messageId
        });

        await processor.ProcessPendingAsync();

        var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId);
        row!.Status.Should().Be(OutboxStatus.Failed);
        row.VisibleAfter.Should().Be(PostgreSqlTestInfrastructure.BaseTime.AddMinutes(2));
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenMaxAttemptsExceeded_ShouldMoveToDeadLetter()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options);

        await using var provider = BuildProvider(
            _fixture,
            options,
            recorder: null,
            useFailingDispatcher: true,
            maxAttempts: 1);

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
        {
            Id = messageId
        });

        await processor.ProcessPendingAsync();

        var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId);
        row!.Status.Should().Be(OutboxStatus.DeadLettered);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenLeaseExpires_ShouldReclaimAndPublishMessage()
    {
        var clock = new PostgreSqlTestInfrastructure.ManualTimeProvider(PostgreSqlTestInfrastructure.BaseTime);
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options);
        var recorder = new EventRecorder();

        await using var provider = BuildProvider(_fixture, options, recorder, clock);
        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var leaseStore = provider.GetRequiredService<IOutboxLeaseStore>();
        var messageId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await outbox.AddAsync(new OrderSubmittedIntegrationEvent { OrderId = orderId }, new OutboxOptions
        {
            Id = messageId
        });

        await leaseStore.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "stale-publisher",
            Now = PostgreSqlTestInfrastructure.BaseTime,
            LeaseDuration = TimeSpan.FromSeconds(20)
        });

        clock.Advance(TimeSpan.FromMinutes(1));
        await processor.ProcessPendingAsync();

        recorder.Events.Should().ContainSingle(@event => @event.OrderId == orderId);

        var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId);
        row!.Status.Should().Be(OutboxStatus.Published);
        row.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task AddAsync_WithVisibleAfter_ShouldDeferPublishingUntilDue()
    {
        var clock = new PostgreSqlTestInfrastructure.ManualTimeProvider(PostgreSqlTestInfrastructure.BaseTime);
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options);
        var recorder = new EventRecorder();
        var visibleAfter = PostgreSqlTestInfrastructure.BaseTime.AddHours(1);

        await using var provider = BuildProvider(_fixture, options, recorder, clock);
        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
        {
            Id = messageId,
            VisibleAfter = visibleAfter
        });

        await processor.ProcessPendingAsync();
        recorder.Events.Should().BeEmpty();

        clock.Advance(TimeSpan.FromHours(1));
        await processor.ProcessPendingAsync();

        recorder.Events.Should().ContainSingle();
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

        services.AddLiteBus(configuration =>
        {
            configuration.AddPostgreSqlOutboxStorage(postgres =>
            {
                postgres.UseDataSource(fixture.DataSource);
                postgres.UseOptions(options);
            });

            configuration.AddEventModule(builder =>
            {
                builder.Register<OrderSubmittedEventHandler>();
            });

            configuration.AddOutboxModule(builder =>
            {
                builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);

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
            });

            if (!useFailingDispatcher)
            {
                configuration.AddOutboxEventDispatcher();
            }
        });

        if (clock is not null)
        {
            services.AddSingleton<TimeProvider>(clock);
        }

        return services.BuildServiceProvider();
    }
}

