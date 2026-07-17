using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.DurableMessaging.Abstractions.Processing;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LiteBus.Inbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

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
        await EfCoreInboxE2eSupport.EnsureInboxTableAsync(_fixture.ConnectionString, storeOptions).ConfigureAwait(false);

         var provider = EfCoreInboxE2eSupport.BuildProvider<AfterDispatchInboxDbContext>(             _fixture.ConnectionString,             storeOptions,             new InboxE2eComposition             {                 Recorder = new CommandRecorder(),                 LeaseOwner = "efcore-inbox-after-dispatch"             });
         await using (provider.ConfigureAwait(true))
         {

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

        var receipt = await scheduler.AcceptAsync(new ShipOrderCommand { OrderId = Guid.NewGuid() }).ConfigureAwait(false);
        var result = await processor.ProcessPendingAsync().ConfigureAwait(false);

        result.DeadLetteredCount.Should().Be(1);

        var row = await EfCoreInboxTableReaders.ReadInboxAsync(_fixture.ConnectionString, storeOptions, receipt.Id).ConfigureAwait(false);
        row!.Status.Should().Be(InboxStatus.DeadLettered);
        row.LastError.Should().Contain("AfterDispatch failed");
        }
    }

    [Fact]
    public async Task PersistAsync_when_completed_already_persisted_should_skip_dead_letter_transition()
    {
        var storeOptions = EfCoreInboxE2eSupport.CreateStoreOptions(TableName + "_fsm");
        await EfCoreInboxE2eSupport.EnsureInboxTableAsync(_fixture.ConnectionString, storeOptions).ConfigureAwait(false);

         var provider = EfCoreInboxE2eSupport.BuildProvider<FsmInboxDbContext>(             _fixture.ConnectionString,             storeOptions,             new InboxE2eComposition());
         await using (provider.ConfigureAwait(true))
         {

        var processingStore = provider.GetRequiredService<IInboxProcessingStore>();
        var scheduler = provider.GetRequiredService<IInbox>();
        var receipt = await scheduler.AcceptAsync(new ShipOrderCommand { OrderId = Guid.NewGuid() }).ConfigureAwait(false);

        var leased = await processingStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "fsm-worker",
            Now = EfCoreInboxE2eSupport.BaseTime,
            LeaseDuration = TimeSpan.FromMinutes(1)
        }).ConfigureAwait(false);

        var completed = leased[0].AsCompleted();
        var completedResult = await processingStore.PersistAsync([completed]).ConfigureAwait(false);
        completedResult.AppliedCount.Should().Be(1);

        var deadLettered = completed with
        {
            Status = InboxStatus.DeadLettered,
            LastError = "hook failure"
        };

        var deadLetterResult = await processingStore.PersistAsync([deadLettered]).ConfigureAwait(false);

        deadLetterResult.AppliedCount.Should().Be(0);
        deadLetterResult.SkippedCount.Should().Be(1);

        var row = await EfCoreInboxTableReaders.ReadInboxAsync(_fixture.ConnectionString, storeOptions, receipt.Id).ConfigureAwait(false);
        row!.Status.Should().Be(InboxStatus.Completed);
        }
    }

    private sealed class AfterDispatchInboxDbContext : EfCoreInboxE2eDbContext
    {
        public AfterDispatchInboxDbContext(DbContextOptions<AfterDispatchInboxDbContext> options, EntityFrameworkCoreInboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }

    private sealed class FsmInboxDbContext : EfCoreInboxE2eDbContext
    {
        public FsmInboxDbContext(DbContextOptions<FsmInboxDbContext> options, EntityFrameworkCoreInboxStoreOptions storeOptions)
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
