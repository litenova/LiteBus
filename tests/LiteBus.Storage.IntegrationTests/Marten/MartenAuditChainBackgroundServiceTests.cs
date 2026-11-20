using System.Data;
using LiteBus.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Engine;
using LiteBus.Auditing.Hashing;
using LiteBus.Auditing.Linking;
using LiteBus.Auditing.Storage.Marten;
using LiteBus.Storage.IntegrationTests.PostgreSql;
using Marten;
using Microsoft.Extensions.DependencyInjection;

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
        var checkpointed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new SignallingCheckpointPublisher(() => checkpointed.TrySetResult());

        var business = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using var configuredBusiness = business.ConfigureAwait(false);
        var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore);
        var segmenter = new MartenAuditRecordSegmenter();
        var moduleBuilder = new AuditingModuleBuilder();
        moduleBuilder.UseHashChain(chain => chain
            .UseScheme(MartenAuditHashSchemes.Sha256V1.Name)
            .CheckpointWhen(CheckpointPolicies.Every(TimeSpan.FromMilliseconds(50)))
            .PublishCheckpointsTo<SignallingCheckpointPublisher>());
        var chainOptions = moduleBuilder.Options.Chain!;
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton<IAuditCanonicalizer<MartenAuditRecord>, MartenAuditRecordCanonicalizer>();
        services.AddSingleton(publisher);
        var provider = services.BuildServiceProvider();
        await using var configuredProvider = provider.ConfigureAwait(false);

        var trail = new MartenAuditTrail<MartenAuditRecord>(store, business, new MartenAuditRecordMapper(), segmenter);
        await trail.WriteAsync(FailedRecord("orders.place-order", category)).ConfigureAwait(false);
        var linker = new AuditLinker<MartenAuditRecord>(
            store,
            segmenter,
            new AuditHashSchemeRegistry(provider),
            chainOptions);
        await linker.LinkPendingAsync(segment, CancellationToken.None).ConfigureAwait(false);

        var lifecycle = new CheckpointLifecycle<MartenAuditRecord>(store, chainOptions, provider);
        var hostServices = new ServiceCollection();
        hostServices.AddSingleton<ICheckpointLifecycle>(lifecycle);
        var hostProvider = hostServices.BuildServiceProvider();
        await using var configuredHostProvider = hostProvider.ConfigureAwait(false);
        var service = new CheckpointPublishBackgroundService(
            hostProvider,
            options: new CheckpointPublishHostOptions { PollInterval = TimeSpan.FromMilliseconds(50) });

        using var stopping = new CancellationTokenSource();
        var run = service.ExecuteAsync(stopping.Token);

        await checkpointed.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        stopping.Cancel();
        await run.ConfigureAwait(false);

        // The loop cuts on one pass and publishes on the next, so anything cut after the wait
        // is published here before the counts are compared.
        await lifecycle.PublishPendingAsync(CancellationToken.None).ConfigureAwait(false);

        publisher.Published.Should().NotBeEmpty();
        publisher.Published.Should().OnlyContain(checkpoint => checkpoint.Segment == segment);

        var stored = await FindCheckpointsAsync(documentStore, segment).ConfigureAwait(false);

        // Delivery is at least once: stopping the loop can abort the publish mark after the publisher
        // ran, so the final publish above can deliver the same checkpoint again. Compare identities.
        stored.Select(checkpoint => checkpoint.Id)
            .Should()
            .BeEquivalentTo(publisher.Published.Select(checkpoint => checkpoint.Id).Distinct());
    }

    [SkippableFact]
    public async Task Service_without_a_registered_lifecycle_returns_immediately()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        await using var configuredProvider = provider.ConfigureAwait(false);
        var service = new CheckpointPublishBackgroundService(provider);
        var run = service.ExecuteAsync(CancellationToken.None);

        await run.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        await run.ConfigureAwait(false);
    }

    private DocumentStore CreateDocumentStore()
    {
        return DocumentStore.For(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = "marten_auditing_checkpoint_loop_tests";
        });
    }

    private static AuditRecord FailedRecord(string action, string? category)
    {
        return new AuditRecord
        {
            Id = Guid.NewGuid(),
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
