using System.Collections.Concurrent;
using System.Reflection;
using LiteBus.Messaging.Abstractions;
using LiteBus.DurableMessaging.Abstractions.Processing;
using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Loads and persists saga state around durable message dispatch.
/// </summary>
public sealed class SagaProcessorHook : IProcessorEnvelopeHook
{
    /// <summary>
    ///     The maximum number of optimistic concurrency attempts for completion-only dispatches.
    /// </summary>
    private const int MaxCompletionAttempts = 3;

    /// <summary>
    ///     Cached invokers that close the generic serializer call over runtime saga state types.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, SerializeStateInvoker> SerializeInvokers = new();

    /// <summary>
    ///     Serializes saga dispatches that target the same tenant, definition, and correlation.
    /// </summary>
    private readonly object _correlationGateSync = new();

    /// <summary>
    ///     Tracks correlation gates and waiters while dispatches are active.
    /// </summary>
    private readonly Dictionary<string, CorrelationGate> _correlationGates = new(StringComparer.Ordinal);

    /// <summary>
    ///     Maps active message identifiers to the correlation gate held by their dispatch scope.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, (string Key, CorrelationGate Gate)> _dispatchGates = new();

    /// <summary>
    ///     Tracks durable messages whose saga mutation was already applied so the dispatcher is not invoked again.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, byte> _suppressedDispatches = new();

    /// <summary>
    ///     Gets the ambient saga context exposed to handlers.
    /// </summary>
    private readonly SagaExecutionContext _context;

    /// <summary>
    ///     Gets the durable saga store.
    /// </summary>
    private readonly ISagaStore _sagaStore;

    /// <summary>
    ///     Gets the serializer used to snapshot saga state for change detection.
    /// </summary>
    private readonly IMessageSerializer _serializer;

    /// <summary>
    ///     Gets the registry that maps saga definition identifiers to state types.
    /// </summary>
    private readonly ISagaStateTypeRegistry _stateTypeRegistry;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SagaProcessorHook" /> class.
    /// </summary>
    /// <param name="sagaStore">The durable saga store.</param>
    /// <param name="stateTypeRegistry">The registry that maps saga definition identifiers to state types.</param>
    /// <param name="serializer">The serializer used to snapshot saga state for change detection.</param>
    /// <param name="context">The ambient saga context exposed to handlers.</param>
    public SagaProcessorHook(
        ISagaStore sagaStore,
        ISagaStateTypeRegistry stateTypeRegistry,
        IMessageSerializer serializer,
        SagaExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(sagaStore);
        ArgumentNullException.ThrowIfNull(stateTypeRegistry);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(context);
        _sagaStore = sagaStore;
        _stateTypeRegistry = stateTypeRegistry;
        _serializer = serializer;
        _context = context;
    }

    /// <inheritdoc />
    public async Task BeforeDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        _context.Reset();

        if (string.IsNullOrWhiteSpace(envelope.CorrelationId))
        {
            return;
        }

        var sagaDefinitionId = _stateTypeRegistry.ResolveDefinitionId(envelope.ContractName);

        if (sagaDefinitionId is null)
        {
            return;
        }

        var stateType = _stateTypeRegistry.ResolveStateType(sagaDefinitionId);

        if (stateType is null)
        {
            return;
        }

        var correlation = CreateCorrelation(envelope, sagaDefinitionId);
        var correlationKey = CreateCorrelationKey(correlation);
        var correlationGate = AcquireCorrelationGate(correlationKey);

        try
        {
            await correlationGate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            _dispatchGates[envelope.MessageId] = (correlationKey, correlationGate);
        }
        catch
        {
            ReleaseCorrelationGate(correlationKey, correlationGate);
            throw;
        }

        try
        {
            var initialState = Activator.CreateInstance(stateType)
                ?? throw new InvalidOperationException($"Could not create saga state type '{stateType.FullName}'.");
            var initialSnapshot = await SerializeStateAsync(stateType, initialState, cancellationToken).ConfigureAwait(false);
            _context.Begin(envelope.MessageId, correlation, initialState, 0, initialSnapshot);
        }
        catch
        {
            ReleaseDispatchGate(envelope.MessageId);
            throw;
        }

        try
        {
            var loaded = await SagaStoreInvoker.LoadAsync(_sagaStore, stateType, correlation, cancellationToken)
                .ConfigureAwait(false);

            if (loaded?.LastAppliedMessageId == envelope.MessageId || loaded?.IsCompleted == true)
            {
                _suppressedDispatches[envelope.MessageId] = 0;
                _context.Reset();
                ReleaseDispatchGate(envelope.MessageId);
                return;
            }

            if (loaded is not null)
            {
                var loadedSnapshot = await SerializeStateAsync(stateType, loaded.Value.State, cancellationToken).ConfigureAwait(false);
                _context.RefreshLoadedState(loaded.Value.State, loaded.Value.Version, loadedSnapshot);
            }
        }
        catch
        {
            _context.Reset();
            ReleaseDispatchGate(envelope.MessageId);
            throw;
        }
    }

    /// <inheritdoc />
    public void PrepareDispatchScope(IProcessorEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        TryAttachScope(envelope);
    }

    /// <inheritdoc />
    public bool ShouldDispatch(IProcessorEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return !_suppressedDispatches.TryRemove(envelope.MessageId, out _);
    }

    /// <inheritdoc />
    public void AbandonDispatchScope(IProcessorEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        try
        {
            _suppressedDispatches.TryRemove(envelope.MessageId, out _);

            if (_context.TryAttach(envelope.MessageId))
            {
                _context.Reset();
            }
        }
        finally
        {
            ReleaseDispatchGate(envelope.MessageId);
        }
    }

    /// <inheritdoc />
    public async Task AfterDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        TryAttachScope(envelope);

        if (!_context.IsActive || _context.Correlation is null)
        {
            _context.Reset();
            return;
        }

        try
        {
            await PersistActiveScopeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _context.Reset();
            ReleaseDispatchGate(envelope.MessageId);
        }
    }

    /// <summary>
    ///     Persists dirty state and completion for the active saga scope.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when persistence succeeds.</returns>
    private async Task PersistActiveScopeAsync(CancellationToken cancellationToken)
    {
        if (_context.Correlation is null)
        {
            return;
        }

        var state = _context.GetActiveState();
        string? currentStateJson = null;

        if (!_context.IsDirty && _context.StateObserved)
        {
            currentStateJson = await SerializeStateAsync(state.GetType(), state, cancellationToken).ConfigureAwait(false);
        }

        var hasChanges = _context.HasChanges(currentStateJson);

        if (hasChanges && _context.ShouldComplete)
        {
            throw new InvalidOperationException(
                "Saga scope cannot call Complete() and change state in the same dispatch. Persist final state first, then complete on a later message.");
        }

        if (hasChanges)
        {
            await SagaStoreInvoker.SaveAsync(
                    _sagaStore,
                    state.GetType(),
                    _context.Correlation,
                    state,
                    _context.Version,
                    _context.DispatchId,
                    cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        if (_context.ShouldComplete)
        {
            await PersistCompletionAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Persists completion intent, reloading only when a concurrent writer advanced the saga version.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when the saga is completed or already completed.</returns>
    /// <remarks>
    ///     Completion is idempotent and can be retried against a newer version. Dirty state is never retried here because
    ///     the hook cannot safely merge an arbitrary handler-owned state snapshot with a concurrent update.
    /// </remarks>
    private async Task PersistCompletionAsync(CancellationToken cancellationToken)
    {
        if (_context.Correlation is null)
        {
            return;
        }

        var correlation = _context.Correlation;
        var stateType = _stateTypeRegistry.ResolveStateType(correlation.SagaDefinitionId);

        if (stateType is null)
        {
            throw new InvalidOperationException(
                $"Saga definition '{correlation.SagaDefinitionId}' is not registered.");
        }

        for (var attempt = 0; attempt < MaxCompletionAttempts; attempt++)
        {
            try
            {
                await _sagaStore.CompleteAsync(
                        SagaCompleteItem.From(correlation, _context.Version, _context.DispatchId),
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (SagaConcurrencyException) when (attempt < MaxCompletionAttempts - 1)
            {
                var loaded = await SagaStoreInvoker.LoadAsync(_sagaStore, stateType, correlation, cancellationToken)
                    .ConfigureAwait(false);

                if (loaded?.IsCompleted == true)
                {
                    return;
                }

                if (loaded is null)
                {
                    throw new SagaConcurrencyException(correlation);
                }

                var loadedSnapshot = await SerializeStateAsync(stateType, loaded.Value.State, cancellationToken).ConfigureAwait(false);
                _context.RefreshLoadedState(loaded.Value.State, loaded.Value.Version, loadedSnapshot);
            }
        }
    }

    /// <summary>
    ///     Serializes one saga state object with the saga serializer.
    /// </summary>
    /// <param name="stateType">The saga state type that closes the generic serializer call.</param>
    /// <param name="state">The state object.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The serialized state JSON.</returns>
    /// <remarks>
    ///     The generic call is closed over the runtime state type so serializers that depend on the compile-time
    ///     type argument observe the state type rather than <see cref="object" />.
    /// </remarks>
    private async Task<string> SerializeStateAsync(Type stateType, object state, CancellationToken cancellationToken)
    {
        var invoker = SerializeInvokers.GetOrAdd(stateType, CreateSerializeInvoker);
        return await invoker(_serializer, state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates a cached serialize invoker for one state type.
    /// </summary>
    /// <param name="stateType">The saga state type.</param>
    /// <returns>The serialize invoker delegate.</returns>
    private static SerializeStateInvoker CreateSerializeInvoker(Type stateType)
    {
        var method = typeof(IMessageSerializer)
            .GetMethod(nameof(IMessageSerializer.SerializeAsync), BindingFlags.Public | BindingFlags.Instance)!
            .MakeGenericMethod(stateType);

        return (serializer, state, cancellationToken) =>
            (Task<string>)method.Invoke(serializer, [state, cancellationToken])!;
    }

    /// <summary>
    ///     Builds the saga correlation for one processor envelope.
    /// </summary>
    /// <param name="envelope">The processor envelope.</param>
    /// <param name="sagaDefinitionId">The resolved saga definition identifier.</param>
    /// <returns>The saga correlation used by the store.</returns>
    private static SagaCorrelation CreateCorrelation(IProcessorEnvelope envelope, string sagaDefinitionId)
    {
        return new SagaCorrelation
        {
            CorrelationId = envelope.CorrelationId!,
            SagaDefinitionId = sagaDefinitionId,
            TenantId = envelope.TenantId
        };
    }

    /// <summary>
    ///     Re-attaches a dispatch scope created during <see cref="BeforeDispatchAsync" /> for one durable message.
    /// </summary>
    /// <param name="envelope">The processor envelope.</param>
    private void TryAttachScope(IProcessorEnvelope envelope)
    {
        _context.TryAttach(envelope.MessageId);
    }

    /// <summary>
    ///     Creates a stable in-process lock key for one saga instance.
    /// </summary>
    /// <param name="correlation">The saga correlation.</param>
    /// <returns>The lock key.</returns>
    private static string CreateCorrelationKey(SagaCorrelation correlation)
    {
        return $"{correlation.TenantId}\u001f{correlation.SagaDefinitionId}\u001f{correlation.CorrelationId}";
    }

    /// <summary>
    ///     Gets or creates a correlation gate and records one active waiter.
    /// </summary>
    /// <param name="key">The correlation lock key.</param>
    /// <returns>The gate registration.</returns>
    private CorrelationGate AcquireCorrelationGate(string key)
    {
        lock (_correlationGateSync)
        {
            if (!_correlationGates.TryGetValue(key, out var gate))
            {
                gate = new CorrelationGate();
                _correlationGates[key] = gate;
            }

            gate.WaiterCount++;
            return gate;
        }
    }

    /// <summary>
    ///     Releases the semaphore and removes an unused correlation gate.
    /// </summary>
    /// <param name="messageId">The active message identifier.</param>
    private void ReleaseDispatchGate(Guid messageId)
    {
        if (_dispatchGates.TryRemove(messageId, out var registration))
        {
            registration.Gate.Semaphore.Release();
            ReleaseCorrelationGate(registration.Key, registration.Gate);
        }
    }

    /// <summary>
    ///     Removes one waiter registration and disposes an unused gate.
    /// </summary>
    /// <param name="key">The correlation lock key.</param>
    /// <param name="gate">The correlation gate.</param>
    private void ReleaseCorrelationGate(string key, CorrelationGate gate)
    {
        lock (_correlationGateSync)
        {
            gate.WaiterCount--;
            if (gate.WaiterCount == 0 && _correlationGates.Remove(key))
            {
                gate.Semaphore.Dispose();
            }
        }
    }

    /// <summary>
    ///     Delegate that serializes saga state for one runtime-resolved state type.
    /// </summary>
    /// <param name="serializer">The message serializer.</param>
    /// <param name="state">The state object.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The serialized state JSON.</returns>
    private delegate Task<string> SerializeStateInvoker(
        IMessageSerializer serializer,
        object state,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Tracks one correlation semaphore and its active waiters.
    /// </summary>
    private sealed class CorrelationGate
    {
        /// <summary>
        ///     Gets the semaphore that serializes one saga correlation.
        /// </summary>
        internal SemaphoreSlim Semaphore { get; } = new(1, 1);

        /// <summary>
        ///     Gets or sets the number of active owners and waiters.
        /// </summary>
        internal int WaiterCount { get; set; }

    }
}
