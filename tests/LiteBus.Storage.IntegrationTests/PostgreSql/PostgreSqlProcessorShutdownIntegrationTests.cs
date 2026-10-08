using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies a shutdown cancelled mid-pass leaves the PostgreSQL inbox and outbox rows releasable without spending an
///     attempt, and that a later pass completes the same rows.
/// </summary>
public sealed class PostgreSqlProcessorShutdownIntegrationTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlProcessorShutdownIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL test fixture.</param>
    public PostgreSqlProcessorShutdownIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms an outbox pass cancelled mid-dispatch refunds the lease attempt and the next pass publishes the row.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_WhenOutboxDispatchCancelledMidPass_ShouldRefundAttemptAndPublishOnNextPass()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.events.shutdown",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        }).ConfigureAwait(false);

        using var firstPass = new CancellationTokenSource();
        var dispatcher = new CancelOnFirstDispatchOutboxDispatcher(firstPass);
        var processor = new PipelinedOutboxProcessor(
            store,
            store,
            dispatcher,
            new OutboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "shutdown-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                LeaseHeartbeatInterval = TimeSpan.Zero,
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { UseJitter = false }
            },
            TimeProvider.System,
            []);

        var act = () => processor.ProcessPendingAsync(firstPass.Token);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        var cancelledRow = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId).ConfigureAwait(false);
        cancelledRow.Should().NotBeNull();
        cancelledRow!.Status.Should().Be(OutboxStatus.Pending);
        cancelledRow.AttemptCount.Should().Be(0);
        cancelledRow.LeaseOwner.Should().BeNull();
        cancelledRow.LeaseExpiresAt.Should().BeNull();
        cancelledRow.LastError.Should().BeNull();

        using var secondPass = new CancellationTokenSource();
        await processor.ProcessPendingAsync(secondPass.Token).ConfigureAwait(false);

        dispatcher.DispatchCount.Should().Be(2);

        var publishedRow = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId).ConfigureAwait(false);
        publishedRow.Should().NotBeNull();
        publishedRow!.Status.Should().Be(OutboxStatus.Published);
        publishedRow.AttemptCount.Should().Be(1);
    }

    /// <summary>
    ///     Confirms an inbox pass cancelled mid-dispatch refunds the lease attempt and the next pass completes the row.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_WhenInboxDispatchCancelledMidPass_ShouldRefundAttemptAndCompleteOnNextPass()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlInboxStore(_fixture.DataSource, options);
        var now = DateTimeOffset.UtcNow;
        var messageId = Guid.NewGuid();

        await store.AddAsync(new InboxEnvelope
        {
            Id = messageId,
            ContractName = "tests.commands.shutdown",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        }).ConfigureAwait(false);

        using var firstPass = new CancellationTokenSource();
        var dispatcher = new CancelOnFirstDispatchInboxDispatcher(firstPass);
        var processor = new PipelinedInboxProcessor(
            store,
            store,
            dispatcher,
            new InboxProcessorOptions
            {
                BatchSize = 1,
                LeaseOwner = "shutdown-worker",
                LeaseDuration = TimeSpan.FromMinutes(1),
                LeaseHeartbeatInterval = TimeSpan.Zero,
                DispatcherConcurrency = 1,
                Retry = new RetryOptions { UseJitter = false }
            },
            TimeProvider.System,
            []);

        var act = () => processor.ProcessPendingAsync(firstPass.Token);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        var cancelledRow = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, messageId).ConfigureAwait(false);
        cancelledRow.Should().NotBeNull();
        cancelledRow!.Status.Should().Be(InboxStatus.Pending);
        cancelledRow.AttemptCount.Should().Be(0);
        cancelledRow.LeaseOwner.Should().BeNull();
        cancelledRow.LeaseExpiresAt.Should().BeNull();
        cancelledRow.LastError.Should().BeNull();

        using var secondPass = new CancellationTokenSource();
        await processor.ProcessPendingAsync(secondPass.Token).ConfigureAwait(false);

        dispatcher.DispatchCount.Should().Be(2);

        var completedRow = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, messageId).ConfigureAwait(false);
        completedRow.Should().NotBeNull();
        completedRow!.Status.Should().Be(InboxStatus.Completed);
        completedRow.AttemptCount.Should().Be(1);
    }

    private sealed class CancelOnFirstDispatchOutboxDispatcher : IOutboxDispatcher
    {
        private readonly CancellationTokenSource _pass;
        private int _dispatchCount;

        public CancelOnFirstDispatchOutboxDispatcher(CancellationTokenSource pass)
        {
            ArgumentNullException.ThrowIfNull(pass);
            _pass = pass;
        }

        public int DispatchCount => _dispatchCount;

        public Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _dispatchCount) == 1)
            {
                _pass.Cancel();
                return Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CancelOnFirstDispatchInboxDispatcher : IInboxDispatcher
    {
        private readonly CancellationTokenSource _pass;
        private int _dispatchCount;

        public CancelOnFirstDispatchInboxDispatcher(CancellationTokenSource pass)
        {
            ArgumentNullException.ThrowIfNull(pass);
            _pass = pass;
        }

        public int DispatchCount => _dispatchCount;

        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _dispatchCount) == 1)
            {
                _pass.Cancel();
                return Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return Task.CompletedTask;
        }
    }
}
