using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

using ICommandInboxProcessor = LiteBus.Inbox.Abstractions.ICommandInboxProcessor;

namespace LiteBus.PostgreSql.IntegrationTests;

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
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);
        var recorder = new CommandRecorder();

        await using var provider = BuildProvider(_fixture, options, recorder);
        var scheduler = provider.GetRequiredService<ICommandScheduler>();
        var processor = provider.GetRequiredService<ICommandInboxProcessor>();

        var orderId = Guid.NewGuid();
        var receipt = await scheduler.ScheduleAsync(new ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        await processor.ProcessPendingAsync();

        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);

        var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, receipt.CommandId);
        row!.Status.Should().Be(InboxCommandStatus.Completed);
        row.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenHandlerFails_ShouldMarkFailedWithVisibleAfter()
    {
        var clock = new PostgreSqlTestInfrastructure.ManualTimeProvider(PostgreSqlTestInfrastructure.BaseTime);
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);

        await using var provider = BuildProvider(
            _fixture,
            options,
            recorder: null,
            clock: clock,
            registerFaultyHandler: true,
            maxAttempts: 5,
            initialDelay: TimeSpan.FromSeconds(30));

        var scheduler = provider.GetRequiredService<ICommandScheduler>();
        var processor = provider.GetRequiredService<ICommandInboxProcessor>();

        var receipt = await scheduler.ScheduleAsync(new FaultyCommand());
        await processor.ProcessPendingAsync();

        var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, receipt.CommandId);
        row!.Status.Should().Be(InboxCommandStatus.Failed);
        row.LastError.Should().NotBeNullOrWhiteSpace();
        row.VisibleAfter.Should().Be(PostgreSqlTestInfrastructure.BaseTime.AddSeconds(30));
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenMaxAttemptsExceeded_ShouldMoveToDeadLetter()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);

        await using var provider = BuildProvider(
            _fixture,
            options,
            recorder: null,
            registerFaultyHandler: true,
            maxAttempts: 1);

        var scheduler = provider.GetRequiredService<ICommandScheduler>();
        var processor = provider.GetRequiredService<ICommandInboxProcessor>();

        var receipt = await scheduler.ScheduleAsync(new FaultyCommand());
        await processor.ProcessPendingAsync();

        var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, receipt.CommandId);
        row!.Status.Should().Be(InboxCommandStatus.DeadLettered);
        row.LastError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenLeaseExpires_ShouldReclaimAndCompleteCommand()
    {
        var clock = new PostgreSqlTestInfrastructure.ManualTimeProvider(PostgreSqlTestInfrastructure.BaseTime);
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        var recorder = new CommandRecorder();

        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);
        await using var provider = BuildProvider(_fixture, options, recorder, clock);
        var scheduler = provider.GetRequiredService<ICommandScheduler>();
        var processor = provider.GetRequiredService<ICommandInboxProcessor>();
        var leaseStore = provider.GetRequiredService<ICommandInboxLeaseStore>();

        var orderId = Guid.NewGuid();
        var receipt = await scheduler.ScheduleAsync(new ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = "lease-expiry"
        });

        await leaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "stale-worker",
            Now = PostgreSqlTestInfrastructure.BaseTime,
            LeaseDuration = TimeSpan.FromSeconds(30)
        });

        clock.Advance(TimeSpan.FromMinutes(1));
        await processor.ProcessPendingAsync();

        recorder.Commands.Should().ContainSingle();
        var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, receipt.CommandId);
        row!.Status.Should().Be(InboxCommandStatus.Completed);
        row.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task ScheduleAsync_WithVisibleAfter_ShouldDeferProcessingUntilDue()
    {
        var clock = new PostgreSqlTestInfrastructure.ManualTimeProvider(PostgreSqlTestInfrastructure.BaseTime);
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        var recorder = new CommandRecorder();
        var visibleAfter = PostgreSqlTestInfrastructure.BaseTime.AddMinutes(30);

        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);
        await using var provider = BuildProvider(_fixture, options, recorder, clock);
        var scheduler = provider.GetRequiredService<ICommandScheduler>();
        var processor = provider.GetRequiredService<ICommandInboxProcessor>();

        var orderId = Guid.NewGuid();
        await scheduler.ScheduleAsync(new ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        }, new CommandScheduleOptions { VisibleAfter = visibleAfter });

        await processor.ProcessPendingAsync();
        recorder.Commands.Should().BeEmpty();

        clock.Advance(TimeSpan.FromMinutes(30));
        await processor.ProcessPendingAsync();

        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenContractIsUnknown_ShouldMarkFailedInDatabase()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);

        await using var provider = BuildProvider(_fixture, options, recorder: null, registerShipHandler: false);
        var processor = provider.GetRequiredService<ICommandInboxProcessor>();
        var writer = provider.GetRequiredService<ICommandInboxWriter>();
        var commandId = Guid.NewGuid();

        await writer.AddAsync(new InboxCommandEnvelope
        {
            CommandId = commandId,
            ContractName = "unknown.contract",
            ContractVersion = 99,
            Payload = "{}",
            CreatedAt = PostgreSqlTestInfrastructure.BaseTime,
            AttemptCount = 0,
            Status = InboxCommandStatus.Pending
        });

        await processor.ProcessPendingAsync();

        var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, commandId);
        row!.Status.Should().Be(InboxCommandStatus.Failed);
        row.LastError.Should().Contain(nameof(MessageContractNotRegisteredException));
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

        services.AddLiteBus(configuration =>
        {
            configuration.AddPostgreSqlCommandInboxStore(postgres =>
            {
                postgres.UseDataSource(fixture.DataSource);
                postgres.UseOptions(options);
            });

            configuration.AddCommandModule(builder =>
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

            configuration.AddCommandInboxModule(builder =>
            {
                if (registerShipHandler)
                {
                    builder.Contracts.Register<ShipOrderCommand>("orders.commands.ship", 1);
                }

                if (registerFaultyHandler)
                {
                    builder.Contracts.Register<FaultyCommand>("orders.commands.faulty", 1);
                }

                builder.UseProcessorOptions(new CommandInboxProcessorOptions
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
            });
        });

        if (clock is not null)
        {
            services.AddSingleton<TimeProvider>(clock);
        }

        return services.BuildServiceProvider();
    }
}
