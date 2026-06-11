using LiteBus.Inbox.Abstractions;
using LiteBus.Orchestration.Abstractions;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Verifies inbox processor AfterDispatch hook failures persist dead-letter outcomes through EF Core storage.
/// </summary>
public sealed class EfCoreInboxProcessorAfterDispatchIntegrationTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string TableName = "inbox_ef_after_dispatch";

    private readonly PostgreSqlFixture _fixture;

    public EfCoreInboxProcessorAfterDispatchIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessPendingAsync_when_after_dispatch_hook_fails_should_dead_letter_from_processing()
    {
        var storeOptions = EfCoreInboxE2eSupport.CreateStoreOptions(TableName);
        await EfCoreInboxE2eSupport.EnsureInboxTableAsync(_fixture.ConnectionString, storeOptions);

        await using var provider = EfCoreInboxE2eSupport.BuildProvider<AfterDispatchInboxDbContext>(
            _fixture.ConnectionString,
            storeOptions,
            new InboxE2eComposition
            {
                Recorder = new CommandRecorder(),
                LeaseOwner = "efcore-inbox-after-dispatch"
            });

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

        var receipt = await scheduler.AcceptAsync(InboxAcceptItems.From(new ShipOrderCommand { OrderId = Guid.NewGuid() }));
        var result = await processor.ProcessPendingAsync();

        result.DeadLetteredCount.Should().Be(1);

        var row = await EfCoreInboxTableReaders.ReadInboxAsync(_fixture.ConnectionString, storeOptions, receipt.Id);
        row!.Status.Should().Be(InboxStatus.DeadLettered);
        row.LastError.Should().Contain("AfterDispatch failed");
    }

    [Fact]
    public async Task PersistAsync_when_completed_already_persisted_should_skip_dead_letter_transition()
    {
        var storeOptions = EfCoreInboxE2eSupport.CreateStoreOptions(TableName + "_fsm");
        await EfCoreInboxE2eSupport.EnsureInboxTableAsync(_fixture.ConnectionString, storeOptions);

        await using var provider = EfCoreInboxE2eSupport.BuildProvider<FsmInboxDbContext>(
            _fixture.ConnectionString,
            storeOptions,
            new InboxE2eComposition());

        var processingStore = provider.GetRequiredService<IInboxProcessingStore>();
        var scheduler = provider.GetRequiredService<IInbox>();
        var receipt = await scheduler.AcceptAsync(InboxAcceptItems.From(new ShipOrderCommand { OrderId = Guid.NewGuid() }));

        var leased = await processingStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "fsm-worker",
            Now = EfCoreInboxE2eSupport.BaseTime,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        var completed = leased[0].AsCompleted();
        var completedResult = await processingStore.PersistAsync([completed]);
        completedResult.AppliedCount.Should().Be(1);

        var deadLettered = completed with
        {
            Status = InboxStatus.DeadLettered,
            LastError = "hook failure"
        };

        var deadLetterResult = await processingStore.PersistAsync([deadLettered]);

        deadLetterResult.AppliedCount.Should().Be(0);
        deadLetterResult.SkippedCount.Should().Be(1);

        var row = await EfCoreInboxTableReaders.ReadInboxAsync(_fixture.ConnectionString, storeOptions, receipt.Id);
        row!.Status.Should().Be(InboxStatus.Completed);
    }

    private sealed class AfterDispatchInboxDbContext : EfCoreInboxE2eDbContext
    {
        public AfterDispatchInboxDbContext(DbContextOptions<AfterDispatchInboxDbContext> options, EfCoreInboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }

    private sealed class FsmInboxDbContext : EfCoreInboxE2eDbContext
    {
        public FsmInboxDbContext(DbContextOptions<FsmInboxDbContext> options, EfCoreInboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }

    private sealed class ThrowingAfterDispatchHook : IProcessorEnvelopeHook
    {
        public Task BeforeDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task AfterDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("AfterDispatch failed.");
        }
    }
}