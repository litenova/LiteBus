using System.Data;
using LiteBus.Commands.Storage.Marten;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Messaging.Audit;
using LiteBus.Storage.IntegrationTests.PostgreSql;
using Marten;

namespace LiteBus.Storage.IntegrationTests.Marten;

/// <summary>
///     Integration tests for the Marten audit checkpoint loop: time-based cuts without new writes.
/// </summary>
public sealed class MartenAuditChainBackgroundServiceTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditChainBackgroundServiceTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public MartenAuditChainBackgroundServiceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task Time_based_policy_cuts_checkpoints_without_new_writes()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var category = $"quiet-{Guid.NewGuid():N}";
        var segment = $"{category}/2026";
        var publishedAfterWrites = 0;
        var checkpointed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SignallingCheckpointPublisher? publisher = null;
        publisher = new SignallingCheckpointPublisher(() =>
        {
            if (publisher!.Published.Count > publishedAfterWrites)
            {
                checkpointed.TrySetResult();
            }
        });
        var options = new MartenAuditTrailOptions
        {
            CheckpointScanInterval = TimeSpan.FromMilliseconds(50)
        };
        options.UseHashChain(chain => chain
            .CheckpointWhen(CheckpointPolicies.Every(TimeSpan.FromMilliseconds(50)))
            .PublishCheckpointsTo(publisher));

        var session = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (session.ConfigureAwait(false))
        {
            var trail = new MartenAuditTrail(session, documentStore, options);
            await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
        }

        publishedAfterWrites = publisher.Published.Count;

        // The setup write may publish its own checkpoint, which must not satisfy the wait for the background service.
        checkpointed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var stopping = new CancellationTokenSource();
        var service = new MartenAuditCheckpointBackgroundService(documentStore, options);
        var run = service.ExecuteAsync(stopping.Token);

        await Task.WhenAny(checkpointed.Task, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
        checkpointed.Task.IsCompleted.Should().BeTrue();

        stopping.Cancel();
        await run.ConfigureAwait(false);

        publisher.Published.Count.Should().BeGreaterThan(publishedAfterWrites);
        publisher.Published.Should().OnlyContain(checkpoint => checkpoint.Segment == segment);

        var stored = await FindCheckpointsAsync(documentStore, segment).ConfigureAwait(false);
        stored.Count.Should().Be(publisher.Published.Count);
    }

    [SkippableFact]
    public async Task Service_without_a_time_based_policy_returns_immediately()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var options = new MartenAuditTrailOptions().UseHashChain();
        var service = new MartenAuditCheckpointBackgroundService(documentStore, options);
        var run = service.ExecuteAsync(CancellationToken.None);

        await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);

        run.IsCompleted.Should().BeTrue();
        await run.ConfigureAwait(false);
    }

    private DocumentStore CreateDocumentStore()
    {
        return DocumentStore.For(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = "marten_commands_audit_chain_tests";
        });
    }

    private static AuditRecord FailedRecord(string action, string? category)
    {
        return new AuditRecord
        {
            Action = action,
            Outcome = AuditOutcome.Failed,
            OccurredAt = new DateTimeOffset(2026, 5, 4, 12, 0, 0, TimeSpan.Zero),
            Category = category,
            FailureCode = "E_ORDER_FAILED"
        };
    }

    private static async Task<IReadOnlyList<MartenAuditCheckpoint>> FindCheckpointsAsync(
        IDocumentStore documentStore,
        string segment)
    {
        var session = documentStore.QuerySession();
        await using (session.ConfigureAwait(false))
        {
            return await session
                .Query<MartenAuditCheckpoint>()
                .Where(checkpoint => checkpoint.Segment == segment)
                .OrderBy(checkpoint => checkpoint.Sequence)
                .ToListAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private sealed class SignallingCheckpointPublisher : ICheckpointPublisher
    {
        private readonly Action _onPublish;

        public SignallingCheckpointPublisher(Action onPublish)
        {
            _onPublish = onPublish;
        }

        public List<AuditCheckpoint> Published { get; } = [];

        public ValueTask PublishAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            Published.Add(checkpoint);
            _onPublish();
            return ValueTask.CompletedTask;
        }
    }
}
