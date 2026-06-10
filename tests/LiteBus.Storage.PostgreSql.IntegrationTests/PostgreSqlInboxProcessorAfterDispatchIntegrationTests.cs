using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Orchestration.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Verifies inbox processor AfterDispatch hook failures persist dead-letter outcomes through PostgreSQL storage.
/// </summary>
public sealed class PostgreSqlInboxProcessorAfterDispatchIntegrationTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlInboxProcessorAfterDispatchIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessPendingAsync_when_after_dispatch_hook_fails_should_dead_letter_from_processing()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);

        await using var provider = BuildProvider(_fixture, options);
        var scheduler = provider.GetRequiredService<IInbox>();
        var processingStore = provider.GetRequiredService<IInboxProcessingStore>();
        var dispatcher = provider.GetRequiredService<IInboxDispatcher>();
        var processorOptions = provider.GetRequiredService<InboxProcessorOptions>();
        var clock = provider.GetRequiredService<TimeProvider>();

        var processor = new PipelinedInboxProcessor(
            processingStore,
            processingStore,
            dispatcher,
            processorOptions,
            clock,
            [new ThrowingAfterDispatchHook()]);

        var messageId = Guid.NewGuid();
        await scheduler.AcceptAsync(new ShipOrderCommand { OrderId = messageId }, new InboxOptions { Id = messageId });

        var result = await processor.ProcessPendingAsync();

        result.DeadLetteredCount.Should().Be(1);

        var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, messageId);
        row!.Status.Should().Be(InboxStatus.DeadLettered);
        row.LastError.Should().Contain("AfterDispatch failed");
    }

    [Fact]
    public async Task PersistAsync_when_completed_already_persisted_should_skip_dead_letter_transition()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);
        var store = new PostgreSqlInboxStore(_fixture.DataSource, options);
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            Status = InboxStatus.Pending,
            AttemptCount = 0
        });

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "fsm-worker",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        var completed = leased[0].AsCompleted();
        var completedResult = await store.PersistAsync([completed]);
        completedResult.AppliedCount.Should().Be(1);

        var deadLettered = completed with
        {
            Status = InboxStatus.DeadLettered,
            LastError = "hook failure"
        };
        var deadLetterResult = await store.PersistAsync([deadLettered]);

        deadLetterResult.AppliedCount.Should().Be(0);
        deadLetterResult.SkippedCount.Should().Be(1);

        var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, messageId);
        row!.Status.Should().Be(InboxStatus.Completed);
    }

    private static ServiceProvider BuildProvider(PostgreSqlFixture fixture, PostgreSqlInboxStoreOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton<CommandRecorder>();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
            registry.AddCommandModule(builder =>
            {
                builder.Register<ShipOrderCommand>();
                builder.Register<ShipOrderCommandHandler>();
            });

            registry.AddInboxModule(builder =>
            {
                builder.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(fixture.DataSource);
                    postgres.UseOptions(options);
                });

                builder.Contracts.Register<ShipOrderCommand>("orders.commands.ship", 1);
                builder.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 1,
                    LeaseOwner = "pg-after-dispatch",
                    LeaseDuration = TimeSpan.FromMinutes(1),
                    Retry = new RetryOptions { UseJitter = false }
                });
                builder.UseCommandInboxDispatcher();
            });
        });

        return services.BuildServiceProvider();
    }

    private sealed class ThrowingAfterDispatchHook : IProcessorEnvelopeHook
    {
        public Task BeforeDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AfterDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("AfterDispatch failed.");
    }
}
