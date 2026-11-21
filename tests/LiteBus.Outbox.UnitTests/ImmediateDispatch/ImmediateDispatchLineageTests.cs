using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.ImmediateDispatch;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.ImmediateDispatch;

/// <summary>
///     Verifies immediate dispatch delivers reactions as root mediations with the same lineage the processor observes.
/// </summary>
/// <remarks>
///     Each delivery runs inside <see cref="RootMediationScope" />, so the reaction carries the envelope cause as data
///     and never inherits the ambient scope of the mediation that staged it. The tests run one reaction through the
///     processor path and through both immediate modes and compare parent, cause, identity, and nesting verdicts.
/// </remarks>
[Collection("Sequential")]
public sealed class ImmediateDispatchLineageTests : LiteBusTestBase
{
    [Fact]
    public async Task Await_mode_delivers_as_a_root_without_the_staging_parent()
    {
        var recorder = new LineageRecorder();
        var provider = BuildProvider(recorder);
        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        var mediator = provider.GetRequiredService<IEventMediator>();

        var envelope = await StageEnvelopeAsync(store, "correlation-await", "causation-await", "tenant-await").ConfigureAwait(false);
        var tracker = new StagedImmediateEnvelopes();
        tracker.Add(envelope.Id);

        var listener = CreateListener(provider, tracker, OutboxImmediateDispatchMode.Await, new RecordingOutboxDispatcher(mediator));

        var staging = new StubLineageContext(typeof(string));

        using (AmbientExecutionContext.CreateScope(staging))
        {
            await listener.OnCommittedAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var observed = recorder.Observed.Should().ContainSingle().Subject;
        observed.Parent.Should().BeNull();
        observed.Identity.TenantId.Should().Be("tenant-await");
    }

    [Fact]
    public async Task Background_mode_delivers_as_a_root_without_the_staging_parent()
    {
        var recorder = new LineageRecorder();
        var provider = BuildProvider(recorder);
        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        var mediator = provider.GetRequiredService<IEventMediator>();

        var envelope = await StageEnvelopeAsync(store, "correlation-background", "causation-background", "tenant-background").ConfigureAwait(false);
        var tracker = new StagedImmediateEnvelopes();
        tracker.Add(envelope.Id);

        var listener = CreateListener(provider, tracker, OutboxImmediateDispatchMode.Background, new RecordingOutboxDispatcher(mediator));

        var staging = new StubLineageContext(typeof(string));

        using (AmbientExecutionContext.CreateScope(staging))
        {
            await listener.OnCommittedAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await recorder.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        var observed = recorder.Observed.Should().ContainSingle().Subject;
        observed.Parent.Should().BeNull();
        observed.Identity.TenantId.Should().Be("tenant-background");
    }

    [Fact]
    public async Task Processor_and_immediate_paths_observe_equal_lineage()
    {
        var processorRecorder = new LineageRecorder();
        var immediateRecorder = new LineageRecorder();
        var processorProvider = BuildProvider(processorRecorder);
        var immediateProvider = BuildProvider(immediateRecorder);

        var processorStore = processorProvider.GetRequiredService<InMemoryOutboxStore>();
        var immediateStore = immediateProvider.GetRequiredService<InMemoryOutboxStore>();
        var processorMediator = processorProvider.GetRequiredService<IEventMediator>();
        var immediateMediator = immediateProvider.GetRequiredService<IEventMediator>();

        const string correlationId = "correlation-shared";
        const string causationId = "causation-shared";
        const string tenantId = "tenant-shared";

        var processorEnvelope = await StageEnvelopeAsync(processorStore, correlationId, causationId, tenantId).ConfigureAwait(false);
        var immediateEnvelope = await StageEnvelopeAsync(immediateStore, correlationId, causationId, tenantId).ConfigureAwait(false);

        AmbientExecutionContext.ResetForTesting();

        var processorDispatcher = new RecordingOutboxDispatcher(processorMediator, MediationCauseSource.Outbox);
        var leasedProcessor = processorEnvelope.AsLeased("processor-test", DateTimeOffset.UtcNow.AddMinutes(5));

        await OutboxProcessorEnvelopeHandler.DispatchAsync(
            leasedProcessor,
            processorDispatcher,
            new OutboxProcessorOptions(),
            TimeProvider.System,
            NullLineageLogger.Instance,
            [],
            CancellationToken.None).ConfigureAwait(false);

        var tracker = new StagedImmediateEnvelopes();
        tracker.Add(immediateEnvelope.Id);

        var immediateDispatcher = new RecordingOutboxDispatcher(immediateMediator, MediationCauseSource.ImmediateDispatch);
        var listener = CreateListener(immediateProvider, tracker, OutboxImmediateDispatchMode.Await, immediateDispatcher);

        var staging = new StubLineageContext(typeof(string));

        using (AmbientExecutionContext.CreateScope(staging))
        {
            await listener.OnCommittedAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var processorObserved = processorRecorder.Observed.Should().ContainSingle().Subject;
        var immediateObserved = immediateRecorder.Observed.Should().ContainSingle().Subject;

        processorObserved.Parent.Should().BeNull();
        immediateObserved.Parent.Should().BeNull();
        processorObserved.Identity.Should().Be(immediateObserved.Identity);

        processorObserved.Cause.Should().NotBeNull();
        immediateObserved.Cause.Should().NotBeNull();
        processorObserved.Cause!.CorrelationId.Should().Be(immediateObserved.Cause!.CorrelationId);
        processorObserved.Cause.CausationId.Should().Be(immediateObserved.Cause.CausationId);

        EvaluateNesting(processorObserved).Should().Be(EvaluateNesting(immediateObserved));
    }

    [Fact]
    public async Task A_nested_send_from_an_immediate_reaction_sees_no_staging_ancestor()
    {
        var recorder = new LineageRecorder();
        var provider = BuildProvider(recorder);
        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        var mediator = provider.GetRequiredService<IEventMediator>();

        var envelope = await StageEnvelopeAsync(store, "correlation-nesting", "causation-nesting", null).ConfigureAwait(false);
        var tracker = new StagedImmediateEnvelopes();
        tracker.Add(envelope.Id);

        var listener = CreateListener(provider, tracker, OutboxImmediateDispatchMode.Await, new RecordingOutboxDispatcher(mediator));

        var staging = new StubLineageContext(typeof(string));

        using (AmbientExecutionContext.CreateScope(staging))
        {
            await listener.OnCommittedAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var observed = recorder.Observed.Should().ContainSingle().Subject;
        observed.Parent.Should().BeNull();

        var rule = new DenyEventUnderStagingRule();
        var verdict = rule.Evaluate(new MediationNestingContext(observed.Context, typeof(ImmediateLineageEvent)));

        verdict.Should().Be(Verdict.Allow);
    }

    /// <summary>
    ///     Evaluates the staging-ancestor rule for a hypothetical nested send from the observed reaction.
    /// </summary>
    /// <param name="observed">The lineage the reaction observed.</param>
    /// <returns>The rule verdict.</returns>
    private static Verdict EvaluateNesting(LineageObservation observed)
    {
        var rule = new DenyEventUnderStagingRule();

        return rule.Evaluate(new MediationNestingContext(observed.Context, typeof(ImmediateLineageEvent)));
    }

    /// <summary>
    ///     Stages one envelope in the in-memory store and returns it.
    /// </summary>
    /// <param name="store">The store receiving the envelope.</param>
    /// <param name="correlationId">The correlation identifier recorded on the envelope.</param>
    /// <param name="causationId">The causation identifier recorded on the envelope.</param>
    /// <param name="tenantId">The tenant recorded on the envelope, or <see langword="null" /> for none.</param>
    /// <returns>The staged envelope.</returns>
    private static async Task<OutboxEnvelope> StageEnvelopeAsync(
        InMemoryOutboxStore store,
        string correlationId,
        string causationId,
        string? tenantId)
    {
        ArgumentNullException.ThrowIfNull(store);

        var envelope = new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "lineage.probe",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            CorrelationId = correlationId,
            CausationId = causationId,
            TenantId = tenantId
        };

        await store.AddAsync(envelope, CancellationToken.None).ConfigureAwait(false);

        return envelope;
    }

    /// <summary>
    ///     Builds a provider registering the lineage probe event and its handler.
    /// </summary>
    /// <param name="recorder">The recorder shared with the handler.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(LineageRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddEvents(builder =>
                {
                    builder.Register<ImmediateLineageEvent>();
                    builder.Register<ImmediateLineageEventHandler>();
                });

                registry.AddOutbox(builder =>
                {
                    builder.UseInMemoryStorage();
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Creates an immediate dispatch listener over the provider store with the given mode and dispatcher.
    /// </summary>
    /// <param name="provider">The provider supplying the in-memory store.</param>
    /// <param name="tracker">The tracker holding the staged identifiers.</param>
    /// <param name="mode">The immediate dispatch mode.</param>
    /// <param name="dispatcher">The dispatcher publishing each leased envelope.</param>
    /// <returns>The listener.</returns>
    /// <remarks>
    ///     The scope factory resolves nothing, so background dispatch falls back to the supplied dispatcher and
    ///     store instead of the provider's in-process dispatcher.
    /// </remarks>
    private static OutboxImmediateDispatchListener CreateListener(
        ServiceProvider provider,
        StagedImmediateEnvelopes tracker,
        OutboxImmediateDispatchMode mode,
        IOutboxDispatcher dispatcher)
    {
        var store = provider.GetRequiredService<InMemoryOutboxStore>();

        return new OutboxImmediateDispatchListener(
            tracker,
            new OutboxImmediateDispatchOptions { Mode = mode },
            new OutboxProcessorOptions(),
            store,
            store,
            dispatcher,
            TimeProvider.System,
            new EmptyDispatchScopeFactory());
    }
}
