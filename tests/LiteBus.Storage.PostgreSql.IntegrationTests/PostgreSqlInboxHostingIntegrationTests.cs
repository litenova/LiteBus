using System.Text.Json;
using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Integration tests for manifest-hosted inbox background services against PostgreSQL storage.
/// </summary>
public sealed class PostgreSqlInboxHostingIntegrationTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxHostingIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlInboxHostingIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies the processor background service completes an accepted command without calling
    ///     <see cref="IInboxProcessor.ProcessPendingAsync" /> directly.
    /// </summary>
    /// <returns>A task that completes when the hosted processor finishes the command.</returns>
    [Fact]
    public async Task ProcessorBackgroundService_ShouldProcessAcceptedCommandThroughPostgreSqlStore()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);
        var recorder = new CommandRecorder();

        await using var provider = BuildProcessorProvider(options, recorder);
        LiteBusHostedServiceExtensions.AssertBackgroundServices(
            provider,
            typeof(InboxProcessorBackgroundService));

        var scheduler = provider.GetRequiredService<IInbox>();
        var orderId = Guid.NewGuid();
        var receipt = await scheduler.AcceptAsync(new ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);

        try
        {
            await PostgreSqlTestInfrastructure.WaitUntilAsync(
                async () =>
                {
                    var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, receipt.Id);
                    return row?.Status == InboxStatus.Completed && recorder.Commands.Any(command => command.OrderId == orderId);
                },
                TimeSpan.FromSeconds(10));

            recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);

            var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, receipt.Id);
            row!.Status.Should().Be(InboxStatus.Completed);
            row.AttemptCount.Should().Be(1);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
        }
    }

    /// <summary>
    ///     Verifies PostgreSQL <c>NOTIFY</c> wakes the manifest-hosted processor before a long poll interval elapses.
    /// </summary>
    /// <returns>A task that completes when the row is processed via notification wake-up.</returns>
    [Fact]
    public async Task ProcessorBackgroundService_WhenPendingRowInserted_ShouldWakeViaNotifyBeforePollTimeout()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);
        var recorder = new CommandRecorder();
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await using var provider = BuildProcessorProvider(
            options,
            recorder,
            configureHost: host =>
            {
                host.PollInterval = TimeSpan.FromSeconds(30);
                host.UseAdaptivePolling = false;
            });

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), runCts.Token);

            var store = provider.GetRequiredService<IInboxStore>();
            var payload = JsonSerializer.Serialize(
                new ShipOrderCommand { OrderId = orderId, IdempotencyKey = $"ship:{orderId}" },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            await store.EnqueueAsync(new InboxEnvelope
            {
                Id = messageId,
                ContractName = "orders.commands.ship",
                ContractVersion = 1,
                Payload = payload,
                CreatedAt = DateTimeOffset.UtcNow,
                AttemptCount = 0,
                Status = InboxStatus.Pending
            });

            var completedBeforePoll = await PostgreSqlTestInfrastructure.WaitUntilAsync(
                async () =>
                {
                    var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, messageId);
                    return row?.Status == InboxStatus.Completed && recorder.Commands.Any(command => command.OrderId == orderId);
                },
                TimeSpan.FromSeconds(8));

            completedBeforePoll.Should().BeTrue(
                "the insert notify trigger should wake the processor before the 30 second poll interval");

            recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
        }
    }

    /// <summary>
    ///     Verifies the cleanup background service purges completed rows past retention in PostgreSQL storage.
    /// </summary>
    /// <returns>A task that completes when expired rows are deleted by the hosted cleanup loop.</returns>
    [Fact]
    public async Task CleanupBackgroundService_ShouldPurgeCompletedRowsPastRetention()
    {
        var clock = new ManualTimeProvider(PostgreSqlTestInfrastructure.BaseTime);
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);

        await using var provider = BuildCleanupProvider(options, clock);
        LiteBusHostedServiceExtensions.AssertBackgroundServices(
            provider,
            typeof(InboxCleanupBackgroundService));

        var writer = provider.GetRequiredService<IInboxStore>();
        var leaseStore = provider.GetRequiredService<IInboxLeaseStore>();
        var stateWriter = provider.GetRequiredService<IInboxStateWriter>();

        var oldCompletedId = Guid.NewGuid();
        var recentCompletedId = Guid.NewGuid();
        var now = PostgreSqlTestInfrastructure.BaseTime;

        var oldCompletedAt = now.AddHours(-3);
        var recentCompletedAt = now.AddMinutes(-10);
        await SeedCompletedRowAsync(writer, leaseStore, stateWriter, oldCompletedId, oldCompletedAt, oldCompletedAt);
        await SeedCompletedRowAsync(writer, leaseStore, stateWriter, recentCompletedId, recentCompletedAt, recentCompletedAt);

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token);

        try
        {
            var purged = await PostgreSqlTestInfrastructure.WaitUntilAsync(
                async () =>
                {
                    var oldRow = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, oldCompletedId);
                    return oldRow is null;
                },
                TimeSpan.FromSeconds(5));

            purged.Should().BeTrue("the cleanup loop should delete completed rows older than the retention window");

            var recentRow = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, recentCompletedId);
            recentRow.Should().NotBeNull();
            recentRow!.Status.Should().Be(InboxStatus.Completed);

            var coordinator = provider.GetRequiredService<InboxRetentionCoordinator>();
            coordinator.GetStatus().LastError.Should().BeNull();
        }
        finally
        {
            await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None);
        }
    }

    private static async Task SeedCompletedRowAsync(
        IInboxStore writer,
        IInboxLeaseStore leaseStore,
        IInboxStateWriter stateWriter,
        Guid messageId,
        DateTimeOffset createdAt,
        DateTimeOffset completedAt)
    {
        await writer.EnqueueAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.commands.retention",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = createdAt,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        });

        var leased = await leaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "retention-seed",
            Now = completedAt.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await stateWriter.PersistAsync([leased[0].AsCompleted() with { CompletedAt = completedAt }]);
    }

    private ServiceProvider BuildProcessorProvider(
        PostgreSqlInboxStoreOptions options,
        CommandRecorder recorder,
        Action<InboxProcessorHostOptions>? configureHost = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
            registry.AddCommandModule(builder =>
            {
                builder.Register<ShipOrderCommand>();
                builder.Register<ShipOrderCommandHandler>();
            });

            registry.AddInboxModule(inbox =>
            {
                inbox.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(_fixture.DataSource);
                    postgres.UseOptions(options with { UseListenNotify = true });
                    postgres.DisableSchemaInitialization();
                });

                inbox.Contracts.Register<ShipOrderCommand>("orders.commands.ship", 1);
                inbox.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "pg-hosting-test-worker",
                    Retry = new RetryOptions { UseJitter = false }
                });
                inbox.UseCommandInboxDispatcher();
                inbox.EnableInboxProcessor(host =>
                {
                    host.PollInterval = TimeSpan.FromMilliseconds(100);
                    configureHost?.Invoke(host);
                });
            });
        });

        return services.BuildServiceProvider();
    }

    private ServiceProvider BuildCleanupProvider(
        PostgreSqlInboxStoreOptions options,
        ManualTimeProvider clock)
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(message => message.UseTimeProvider(clock));
            registry.AddInboxModule(inbox =>
            {
                inbox.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(_fixture.DataSource);
                    postgres.UseOptions(options with { UseListenNotify = true });
                    postgres.DisableSchemaInitialization();
                });

                inbox.EnableCleanup(cleanup =>
                {
                    cleanup.Enabled = true;
                    cleanup.Interval = TimeSpan.FromMilliseconds(50);
                    cleanup.Retention = TimeSpan.FromHours(1);
                });
            });
        });

        return services.BuildServiceProvider();
    }
}
