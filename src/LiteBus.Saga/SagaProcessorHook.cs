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
    ///     Gets the ambient saga context exposed to handlers.
    /// </summary>
    private readonly SagaExecutionContext _context;

    /// <summary>
    ///     Gets the durable saga store.
    /// </summary>
    private readonly ISagaStore _sagaStore;

    /// <summary>
    ///     Gets the serializer used to hydrate default state objects.
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
    /// <param name="serializer">The serializer used to hydrate default state objects.</param>
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
        var initialState = Activator.CreateInstance(stateType)
            ?? throw new InvalidOperationException($"Could not create saga state type '{stateType.FullName}'.");

        _context.Begin(envelope.MessageId, correlation, initialState, 0);

        try
        {
            var loaded = await SagaStoreInvoker.LoadAsync(_sagaStore, stateType, correlation, cancellationToken)
                .ConfigureAwait(false);

            if (loaded?.IsCompleted == true)
            {
                _context.Reset();
                return;
            }

            if (loaded is not null)
            {
                _context.RefreshLoadedState(loaded.Value.State, loaded.Value.Version);
            }
        }
        catch
        {
            _context.Reset();
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
    public void AbandonDispatchScope(IProcessorEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (_context.TryAttach(envelope.MessageId))
        {
            _context.Reset();
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

        if (_context is { IsDirty: true, ShouldComplete: true })
        {
            throw new InvalidOperationException(
                "Saga scope cannot call Complete() and SetState() in the same dispatch. Persist final state first, then complete on a later message.");
        }

        if (_context.IsDirty)
        {
            var state = _context.GetActiveState();

            await SagaStoreInvoker.SaveAsync(
                    _sagaStore,
                    state.GetType(),
                    _context.Correlation,
                    state,
                    _context.Version,
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
                        SagaCompleteItem.From(correlation, _context.Version),
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

                _context.RefreshLoadedState(loaded.Value.State, loaded.Value.Version);
            }
        }
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
}
