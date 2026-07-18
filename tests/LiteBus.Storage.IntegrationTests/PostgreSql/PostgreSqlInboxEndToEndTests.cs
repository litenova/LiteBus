using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using IInboxProcessor = LiteBus.Inbox.Abstractions.IInboxProcessor;
using LiteBus.Storage.PostgreSql;
using LiteBus.Outbox;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

public sealed class PostgreSqlInboxEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlInboxEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldExecuteScheduledCommandThroughPostgreSqlStore()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var recorder = new CommandRecorder();

         var provider = BuildProvider(_fixture, options, recorder);
         await using (provider.ConfigureAwait(false))
         {
        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var orderId = Guid.NewGuid();

        var receipt = await scheduler.AcceptAsync(new ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        }).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);

        var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, receipt.Id).ConfigureAwait(false);
        row!.Status.Should().Be(InboxStatus.Completed);
        row.AttemptCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenHandlerFails_ShouldMarkFailedWithVisibleAfter()
    {
        var clock = new ManualTimeProvider(PostgreSqlTestInfrastructure.BaseTime);
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);

         var provider = BuildProvider(             _fixture,             options,             null,             clock,             true,             maxAttempts: 5,             initialDelay: TimeSpan.FromSeconds(30));
         await using (provider.ConfigureAwait(true))
         {

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var receipt = await scheduler.AcceptAsync(new FaultyCommand()).ConfigureAwait(false);
        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, receipt.Id).ConfigureAwait(false);
        row!.Status.Should().Be(InboxStatus.Failed);
        row.LastError.Should().NotBeNullOrWhiteSpace();
        row.VisibleAfter.Should().Be(PostgreSqlTestInfrastructure.BaseTime.AddSeconds(30));
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenMaxAttemptsExceeded_ShouldMoveToDeadLetter()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);

         var provider = BuildProvider(             _fixture,             options,             null,             registerFaultyHandler: true,             maxAttempts: 1);
         await using (provider.ConfigureAwait(true))
         {

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var receipt = await scheduler.AcceptAsync(new FaultyCommand()).ConfigureAwait(false);
        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, receipt.Id).ConfigureAwait(false);
        row!.Status.Should().Be(InboxStatus.DeadLettered);
        row.LastError.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenLeaseExpires_ShouldReclaimAndCompleteCommand()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        var recorder = new CommandRecorder();

        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var provider = BuildProvider(_fixture, options, recorder);
        await using (provider.ConfigureAwait(false))
        {
            var scheduler = provider.GetRequiredService<IInbox>();
            var processor = provider.GetRequiredService<IInboxProcessor>();
            var leaseStore = provider.GetRequiredService<IInboxLeaseStore>();

            var orderId = Guid.NewGuid();

            var receipt = await scheduler.AcceptAsync(new ShipOrderCommand
            {
                OrderId = orderId,
                IdempotencyKey = "lease-expiry"
            }).ConfigureAwait(false);

            await leaseStore.LeasePendingAsync(new InboxLeaseRequest
            {
                BatchSize = 1,
                LeaseOwner = "stale-worker",
                Now = PostgreSqlTestInfrastructure.BaseTime,
                LeaseDuration = TimeSpan.FromMinutes(1)
            }).ConfigureAwait(false);

            await PostgreSqlDatabaseTimeTestSupport.ExpireLeaseAsync(
                _fixture.DataSource,
                options.SchemaName,
                options.TableName,
                receipt.Id).ConfigureAwait(false);
            await processor.ProcessPendingAsync().ConfigureAwait(false);

            recorder.Commands.Should().ContainSingle();
            var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, receipt.Id).ConfigureAwait(false);
            row!.Status.Should().Be(InboxStatus.Completed);
            row.AttemptCount.Should().Be(2);
        }
    }

    [Fact]
    public async Task ScheduleAsync_WithVisibleAfter_ShouldDeferProcessingUntilDue()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        var recorder = new CommandRecorder();
        var visibleAfter = DateTimeOffset.UtcNow.AddHours(1);

        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var provider = BuildProvider(_fixture, options, recorder);
        await using (provider.ConfigureAwait(false))
        {
            var scheduler = provider.GetRequiredService<IInbox>();
            var processor = provider.GetRequiredService<IInboxProcessor>();

            var orderId = Guid.NewGuid();

            var receipt = await scheduler.AcceptAsync(InboxAcceptItem<ShipOrderCommand>.ScheduledAt(
                new ShipOrderCommand
                {
                    OrderId = orderId,
                    IdempotencyKey = $"ship:{orderId}"
                },
                visibleAfter)).ConfigureAwait(false);

            await processor.ProcessPendingAsync().ConfigureAwait(false);
            recorder.Commands.Should().BeEmpty();

            await PostgreSqlDatabaseTimeTestSupport.MakeVisibleAsync(
                _fixture.DataSource,
                options.SchemaName,
                options.TableName,
                receipt.Id).ConfigureAwait(false);
            await processor.ProcessPendingAsync().ConfigureAwait(false);

            recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenContractIsUnknown_ShouldMarkFailedInDatabase()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);

         var provider = BuildProvider(_fixture, options, null, registerShipHandler: false);
         await using (provider.ConfigureAwait(false))
         {
        var processor = provider.GetRequiredService<IInboxProcessor>();
        var writer = provider.GetRequiredService<IInboxStore>();
        var commandId = Guid.NewGuid();

        await writer.EnqueueAsync(new InboxEnvelope
        {
            Id = commandId,
            ContractName = "unknown.contract",
            ContractVersion = 99,
            Payload = "{}",
            CreatedAt = PostgreSqlTestInfrastructure.BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, commandId).ConfigureAwait(false);
        row!.Status.Should().Be(InboxStatus.Failed);
        row.LastError.Should().Contain(nameof(MessageContractNotRegisteredException));
        }
    }

    private static ServiceProvider BuildProvider(
        PostgreSqlFixture fixture,
        PostgreSqlInboxStoreOptions options,
        CommandRecorder? recorder,
        TimeProvider? clock = null,
        bool registerFaultyHandler = false,
        bool registerShipHandler = true,
        int maxAttempts = 5,
        TimeSpan? initialDelay = null)
    {
        var services = new ServiceCollection();

        if (recorder is not null)
        {
            services.AddSingleton(recorder);
        }

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(builder =>
            {
                if (registerShipHandler)
                {
                    builder.Register<ShipOrderCommand>();
                    builder.Register<ShipOrderCommandHandler>();
                }

                if (registerFaultyHandler)
                {
                    builder.Register<FaultyCommand>();
                    builder.Register<FaultyCommandHandler>();
                }
            });

            registry.AddInbox(builder =>
            {
                builder.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(fixture.DataSource);
                    postgres.UseOptions(options);
                });

                if (registerShipHandler)
                {
                    builder.Contracts.Register<ShipOrderCommand>("orders.commands.ship");
                }

                if (registerFaultyHandler)
                {
                    builder.Contracts.Register<FaultyCommand>("orders.commands.faulty");
                }

                builder.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "pg-e2e-worker",
                    Retry = new RetryOptions
                    {
                        MaxAttempts = maxAttempts,
                        InitialDelay = initialDelay ?? TimeSpan.Zero,
                        UseJitter = false
                    }
                });

                builder.UseInProcessDispatch();
            });
        });

        if (clock is not null)
        {
            services.AddSingleton<TimeProvider>(clock);
        }

        return services.BuildServiceProvider();
    }
}
