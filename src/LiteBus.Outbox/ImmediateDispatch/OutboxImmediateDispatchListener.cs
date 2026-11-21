using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Outbox.ImmediateDispatch;

/// <summary>
///     Dispatches the immediate outbox envelopes staged by one mediation after its unit of work commits.
/// </summary>
/// <remarks>
///     <para>
///         Registered as a scoped <see cref="IUnitOfWorkCommitListener" /> by
///         <see cref="OutboxModuleBuilder.EnableImmediateDispatch" />. The command and query unit-of-work completion
///         handlers call it after <see cref="IUnitOfWork.CommitAsync" /> returns, and a command batch calls it once
///         after its single commit.
///     </para>
///     <para>
///         A successful dispatch marks the envelope published, so the outbox processor never runs it again. A failed
///         dispatch leaves the envelope to the processor retry. The mediation outcome never changes because the data is
///         already committed.
///     </para>
///     <para>
///         Each delivery runs as a root mediation inside <see cref="RootMediationScope" />, so the reaction carries the
///         envelope cause as data and never inherits the ambient scope of the mediation that staged it. Processor
///         delivery and immediate delivery therefore observe identical lineage.
///     </para>
/// </remarks>
internal sealed class OutboxImmediateDispatchListener : IUnitOfWorkCommitListener
{
    /// <summary>
    ///     The handler label recorded in logs when an envelope targets every subscribed handler.
    /// </summary>
    private const string BroadcastHandlerLabel = "(all)";

    /// <summary>
    ///     Gets the time provider used for lease and visibility timestamps.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the dispatcher used to publish each leased envelope.
    /// </summary>
    private readonly IOutboxDispatcher _dispatcher;

    /// <summary>
    ///     Gets the scope factory used to create the background dispatch scope.
    /// </summary>
    private readonly IMessageDispatchScopeFactory _dispatchScopeFactory;

    /// <summary>
    ///     Gets the processor envelope hooks invoked around dispatch.
    /// </summary>
    private readonly IReadOnlyList<IProcessorEnvelopeHook> _hooks;

    /// <summary>
    ///     Gets the lease store used to claim the staged envelopes by identifier.
    /// </summary>
    private readonly IOutboxTargetedLeaseStore _leaseStore;

    /// <summary>
    ///     Gets the logger used for immediate dispatch diagnostics.
    /// </summary>
    private readonly ILogger<OutboxImmediateDispatchListener> _logger;

    /// <summary>
    ///     Gets the immediate dispatch options controlling timeout, fallback delay, and wait mode.
    /// </summary>
    private readonly OutboxImmediateDispatchOptions _options;

    /// <summary>
    ///     Gets the processor options carrying retry, lease, and hook failure settings.
    /// </summary>
    private readonly OutboxProcessorOptions _processorOptions;

    /// <summary>
    ///     Gets the state writer used to persist terminal envelope transitions.
    /// </summary>
    private readonly IOutboxStateWriter _stateWriter;

    /// <summary>
    ///     Gets the tracker holding the identifiers staged in this mediation scope.
    /// </summary>
    private readonly StagedImmediateEnvelopes _tracker;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxImmediateDispatchListener" /> class.
    /// </summary>
    /// <param name="tracker">The tracker holding the identifiers staged in this mediation scope.</param>
    /// <param name="options">The immediate dispatch options controlling timeout, fallback delay, and wait mode.</param>
    /// <param name="processorOptions">The processor options carrying retry, lease, and hook failure settings.</param>
    /// <param name="leaseStore">The lease store used to claim the staged envelopes by identifier.</param>
    /// <param name="stateWriter">The state writer used to persist terminal envelope transitions.</param>
    /// <param name="dispatcher">The dispatcher used to publish each leased envelope.</param>
    /// <param name="clock">The time provider used for lease and visibility timestamps.</param>
    /// <param name="dispatchScopeFactory">The scope factory used to create the background dispatch scope.</param>
    /// <param name="hooks">The processor envelope hooks invoked around dispatch, or <see langword="null" /> for none.</param>
    /// <param name="logger">The logger used for immediate dispatch diagnostics, or <see langword="null" /> to discard it.</param>
    public OutboxImmediateDispatchListener(
        StagedImmediateEnvelopes tracker,
        OutboxImmediateDispatchOptions options,
        OutboxProcessorOptions processorOptions,
        IOutboxTargetedLeaseStore leaseStore,
        IOutboxStateWriter stateWriter,
        IOutboxDispatcher dispatcher,
        TimeProvider clock,
        IMessageDispatchScopeFactory dispatchScopeFactory,
        IEnumerable<IProcessorEnvelopeHook>? hooks = null,
        ILogger<OutboxImmediateDispatchListener>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(processorOptions);
        ArgumentNullException.ThrowIfNull(leaseStore);
        ArgumentNullException.ThrowIfNull(stateWriter);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(dispatchScopeFactory);

        _tracker = tracker;
        _options = options;
        _processorOptions = processorOptions;
        _leaseStore = leaseStore;
        _stateWriter = stateWriter;
        _dispatcher = dispatcher;
        _clock = clock;
        _dispatchScopeFactory = dispatchScopeFactory;
        _hooks = hooks?.ToArray() ?? [];
        _logger = logger ?? NullLogger<OutboxImmediateDispatchListener>.Instance;
    }

    /// <inheritdoc />
    public Task OnCommittedAsync(CancellationToken cancellationToken = default)
    {
        var staged = _tracker.Drain();

        if (staged.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (_options.Mode == OutboxImmediateDispatchMode.Background)
        {
            using (System.Threading.ExecutionContext.SuppressFlow())
            {
                _ = Task.Run(
                    () => RootMediationScope.RunAsync(() => DispatchInBackgroundAsync(staged), CancellationToken.None),
                    CancellationToken.None);
            }

            return Task.CompletedTask;
        }

        return DispatchAwaitAsync(staged);
    }

    /// <summary>
    ///     Dispatches the staged envelopes before the caller continues.
    /// </summary>
    /// <param name="staged">The staged envelope identifiers, in staging order.</param>
    /// <returns>A task representing the asynchronous dispatch.</returns>
    /// <remarks>
    ///     The caller cancellation token is not used once the commit has happened; <see cref="OutboxImmediateDispatchOptions.Timeout" />
    ///     bounds the wait. The dispatch runs inside <see cref="RootMediationScope" />, so the reactions start as root
    ///     mediations even though the commit listener runs inside the staging mediation's ambient scope.
    /// </remarks>
    private async Task DispatchAwaitAsync(IReadOnlyList<Guid> staged)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using (RootMediationScope.Begin())
            {
                await DispatchCoreAsync(
                    staged,
                    _leaseStore,
                    _stateWriter,
                    _dispatcher,
                    _processorOptions,
                    _options,
                    _clock,
                    _hooks,
                    _logger).ConfigureAwait(false);
            }
        }
        finally
        {
            stopwatch.Stop();
            OutboxImmediateDispatchTelemetry.RecordDuration(stopwatch.Elapsed);
        }
    }

    /// <summary>
    ///     Dispatches the staged envelopes on a background task with its own scope.
    /// </summary>
    /// <param name="staged">The staged envelope identifiers, in staging order.</param>
    /// <returns>A task representing the asynchronous background dispatch.</returns>
    /// <remarks>
    ///     The mediation scope is gone by the time this runs, so every service is resolved from a fresh dispatch scope,
    ///     falling back to the mediation services when the fresh scope has none. A failure is logged and never fails the
    ///     mediation, because the caller has already continued. The caller runs this inside
    ///     <see cref="RootMediationScope" /> with execution-context flow suppressed, so the reactions start as root
    ///     mediations without the staging mediation's ambient scope.
    /// </remarks>
    private async Task DispatchInBackgroundAsync(IReadOnlyList<Guid> staged)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var scope = _dispatchScopeFactory.CreateScope();
            await using var configuredScope = scope.ConfigureAwait(false);

            var provider = scope.ServiceProvider;
            var leaseStore = provider.GetService(typeof(IOutboxTargetedLeaseStore)) as IOutboxTargetedLeaseStore ?? _leaseStore;
            var stateWriter = provider.GetService(typeof(IOutboxStateWriter)) as IOutboxStateWriter ?? _stateWriter;
            var dispatcher = provider.GetService(typeof(IOutboxDispatcher)) as IOutboxDispatcher ?? _dispatcher;
            var processorOptions = provider.GetService(typeof(OutboxProcessorOptions)) as OutboxProcessorOptions ?? _processorOptions;
            var options = provider.GetService(typeof(OutboxImmediateDispatchOptions)) as OutboxImmediateDispatchOptions ?? _options;
            var clock = provider.GetService(typeof(TimeProvider)) as TimeProvider ?? _clock;
            var logger = provider.GetService(typeof(ILogger<OutboxImmediateDispatchListener>)) as ILogger<OutboxImmediateDispatchListener> ?? _logger;
            var hooks = provider.GetService(typeof(IEnumerable<IProcessorEnvelopeHook>)) as IEnumerable<IProcessorEnvelopeHook>;

            await DispatchCoreAsync(
                staged,
                leaseStore,
                stateWriter,
                dispatcher,
                processorOptions,
                options,
                clock,
                hooks?.ToArray() ?? _hooks,
                logger).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A background dispatch runs after the caller continued, so its fault is logged and the outbox processor remains the fallback.
        catch (Exception exception)
        {
            OutboxImmediateDispatchLogMessages.BackgroundDispatchFailed(_logger, exception);
        }
#pragma warning restore CA1031
        finally
        {
            stopwatch.Stop();
            OutboxImmediateDispatchTelemetry.RecordDuration(stopwatch.Elapsed);
        }
    }

    /// <summary>
    ///     Leases the staged envelopes by identifier and dispatches each in staging order.
    /// </summary>
    /// <param name="staged">The staged envelope identifiers, in staging order.</param>
    /// <param name="leaseStore">The lease store used to claim the staged envelopes.</param>
    /// <param name="stateWriter">The state writer used to persist terminal envelope transitions.</param>
    /// <param name="dispatcher">The dispatcher used to publish each leased envelope.</param>
    /// <param name="processorOptions">The processor options carrying retry, lease, and hook failure settings.</param>
    /// <param name="options">The immediate dispatch options controlling the timeout.</param>
    /// <param name="clock">The time provider used for lease and visibility timestamps.</param>
    /// <param name="hooks">The processor envelope hooks invoked around dispatch.</param>
    /// <param name="logger">The logger used for immediate dispatch diagnostics.</param>
    /// <returns>A task representing the asynchronous dispatch.</returns>
    /// <remarks>
    ///     Envelopes the lease cannot claim are counted as skipped. When <see cref="OutboxImmediateDispatchOptions.Timeout" />
    ///     expires, every leased envelope not yet dispatched is released pending with <c>VisibleAfter</c> set to now, so the
    ///     processor takes it at once.
    /// </remarks>
    private static async Task DispatchCoreAsync(
        IReadOnlyList<Guid> staged,
        IOutboxTargetedLeaseStore leaseStore,
        IOutboxStateWriter stateWriter,
        IOutboxDispatcher dispatcher,
        OutboxProcessorOptions processorOptions,
        OutboxImmediateDispatchOptions options,
        TimeProvider clock,
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        ILogger logger)
    {
        var configuredOwner = string.IsNullOrWhiteSpace(processorOptions.LeaseOwner)
            ? $"{Environment.MachineName}:{Environment.ProcessId}"
            : processorOptions.LeaseOwner;

        var leaseOwner = $"{configuredOwner}:{Guid.NewGuid():N}";

        var leased = await leaseStore.LeaseByIdAsync(
            new OutboxTargetedLeaseRequest
            {
                MessageIds = staged,
                LeaseOwner = leaseOwner,
                LeaseDuration = processorOptions.LeaseDuration,
                Now = clock.GetUtcNow(),
                TenantId = processorOptions.TenantId
            },
            CancellationToken.None).ConfigureAwait(false);

        var leasedById = leased.ToDictionary(static envelope => envelope.Id);

        List<OutboxEnvelope> ordered = [];

        foreach (var messageId in staged)
        {
            if (leasedById.TryGetValue(messageId, out var envelope))
            {
                ordered.Add(envelope);
            }
        }

        for (var skipped = 0; skipped < staged.Count - ordered.Count; skipped++)
        {
            OutboxImmediateDispatchTelemetry.RecordSkipped();
        }

        if (ordered.Count == 0)
        {
            return;
        }

        using var timeoutSource = new CancellationTokenSource(options.Timeout);

        for (var index = 0; index < ordered.Count; index++)
        {
            if (timeoutSource.Token.IsCancellationRequested)
            {
                await ReleaseRemainingAsync(ordered, index, leaseStore, clock, logger).ConfigureAwait(false);
                return;
            }

            var envelope = ordered[index];

            var updated = await OutboxProcessorEnvelopeHandler.DispatchAsync(
                envelope,
                dispatcher,
                processorOptions,
                clock,
                logger,
                hooks,
                timeoutSource.Token).ConfigureAwait(false);

            if (updated is null)
            {
                await ReleaseRemainingAsync(ordered, index, leaseStore, clock, logger).ConfigureAwait(false);
                return;
            }

            var terminal = await PersistTerminalOutcomeAsync(
                envelope,
                updated,
                stateWriter,
                processorOptions,
                hooks,
                clock,
                logger,
                timeoutSource.Token).ConfigureAwait(false);

            if (terminal is null)
            {
                OutboxImmediateDispatchTelemetry.RecordSkipped();
                continue;
            }

            if (terminal.Status == OutboxStatus.Published)
            {
                OutboxImmediateDispatchTelemetry.RecordPublished();
                continue;
            }

            OutboxImmediateDispatchTelemetry.RecordFailed();
            OutboxImmediateDispatchLogMessages.ImmediateDispatchFailed(
                logger,
                envelope.Id,
                envelope.ContractName,
                envelope.Handler ?? BroadcastHandlerLabel);
        }
    }

    /// <summary>
    ///     Persists one dispatched envelope, running after-dispatch hooks for published envelopes.
    /// </summary>
    /// <param name="source">The leased envelope before dispatch.</param>
    /// <param name="updated">The post-dispatch envelope.</param>
    /// <param name="stateWriter">The state writer used to persist the terminal transition.</param>
    /// <param name="processorOptions">The processor options carrying the hook failure policy.</param>
    /// <param name="hooks">The processor envelope hooks invoked after dispatch.</param>
    /// <param name="clock">The time provider used for dead-letter timestamps.</param>
    /// <param name="logger">The logger used for hook and persist diagnostics.</param>
    /// <param name="timeoutToken">The timeout token that cancels dispatch when the wait expires.</param>
    /// <returns>
    ///     The persisted terminal envelope, or <see langword="null" /> when the lease was lost and the persist was skipped.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         After-dispatch hook failures surface as unrelated exception types, so the handler stays broad and maps the
    ///         failure through the configured hook failure policy instead of enumerating hook exception types.
    ///     </para>
    ///     <para>
    ///         Terminal persistence follows <see cref="ProcessorOptions.HonorShutdownTokenOnPersist" />: with the default
    ///         <see langword="false" /> it runs past the timeout so a successful dispatch is never lost to an expired wait.
    ///     </para>
    /// </remarks>
    private static async Task<OutboxEnvelope?> PersistTerminalOutcomeAsync(
        OutboxEnvelope source,
        OutboxEnvelope updated,
        IOutboxStateWriter stateWriter,
        OutboxProcessorOptions processorOptions,
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        TimeProvider clock,
        ILogger logger,
        CancellationToken timeoutToken)
    {
        var persistToken = processorOptions.HonorShutdownTokenOnPersist ? timeoutToken : CancellationToken.None;

        if (updated.Status != OutboxStatus.Published)
        {
            var outcome = await stateWriter.PersistAsync([updated], persistToken).ConfigureAwait(false);

            if (outcome.SkippedCount > 0)
            {
                OutboxProcessorLogMessages.TerminalPersistSkipped(logger, updated.Id);
                return null;
            }

            return updated;
        }

        var hookEnvelope = new OutboxProcessorEnvelopeAdapter(source);
        var afterDispatchCompleted = false;
        OutboxEnvelope terminal;

        try
        {
            await ProcessorHookRunner.RunAfterDispatchAsync(hooks, hookEnvelope, persistToken).ConfigureAwait(false);

            afterDispatchCompleted = true;
            terminal = updated;
        }
#pragma warning disable CA1031 // AfterDispatch hook failures surface as unrelated exception types; the hook failure policy maps them.
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (processorOptions.HookFailurePolicy == ProcessorHookFailurePolicy.CompleteDespiteHookFailure)
            {
                OutboxProcessorLogMessages.AfterDispatchCompleted(logger, updated.Id, exception);
                terminal = updated;
            }
            else
            {
                OutboxProcessorLogMessages.AfterDispatchDeadLettered(logger, updated.Id, exception);
                terminal = source.AsDeadLettered(MessageFailure.From(exception, clock.GetUtcNow()));
            }
        }
#pragma warning restore CA1031
        finally
        {
            if (!afterDispatchCompleted)
            {
                ProcessorHookRunner.RunAbandonDispatchScopes(hooks, hookEnvelope);
            }
        }

        var persistResult = await stateWriter.PersistAsync([terminal], persistToken).ConfigureAwait(false);

        if (persistResult.SkippedCount > 0)
        {
            OutboxProcessorLogMessages.TerminalPersistSkipped(logger, updated.Id);
            return null;
        }

        return terminal;
    }

    /// <summary>
    ///     Releases every leased envelope from the timeout index onward for the outbox processor.
    /// </summary>
    /// <param name="ordered">The leased envelopes, in staging order.</param>
    /// <param name="startIndex">The index of the first envelope that was not dispatched.</param>
    /// <param name="leaseStore">The lease store that releases each envelope under its lease owner and generation.</param>
    /// <param name="clock">The time provider used for the release visibility timestamp.</param>
    /// <param name="logger">The logger used for timeout diagnostics.</param>
    /// <returns>A task representing the asynchronous release.</returns>
    /// <remarks>
    ///     Each envelope is released through <see cref="IOutboxTargetedLeaseStore.ReleaseByIdAsync" />, fenced by its
    ///     lease owner and generation, back to pending with <c>VisibleAfter</c> set to now, so the processor takes it at
    ///     once instead of waiting for the fallback delay. A release the fence rejects means another owner already holds
    ///     the envelope, and nothing more is needed.
    /// </remarks>
    private static async Task ReleaseRemainingAsync(
        List<OutboxEnvelope> ordered,
        int startIndex,
        IOutboxTargetedLeaseStore leaseStore,
        TimeProvider clock,
        ILogger logger)
    {
        var now = clock.GetUtcNow();

        for (var index = startIndex; index < ordered.Count; index++)
        {
            var envelope = ordered[index];

            if (envelope.LeaseOwner is not null)
            {
                await leaseStore.ReleaseByIdAsync(
                    new OutboxTargetedReleaseRequest(envelope.Id, envelope.LeaseOwner, envelope.LeaseGeneration, now),
                    CancellationToken.None).ConfigureAwait(false);
            }

            OutboxImmediateDispatchTelemetry.RecordTimedOut();
            OutboxImmediateDispatchLogMessages.ImmediateDispatchTimedOut(
                logger,
                envelope.Id,
                envelope.ContractName,
                envelope.Handler ?? BroadcastHandlerLabel);
        }
    }
}
