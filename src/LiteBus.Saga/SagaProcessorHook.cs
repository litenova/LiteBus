using LiteBus.Messaging.Abstractions;
using LiteBus.Orchestration.Abstractions;
using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Loads and persists saga state around durable message dispatch.
/// </summary>
public sealed class SagaProcessorHook : IProcessorEnvelopeHook
{
    /// <summary>
    ///     The maximum number of optimistic concurrency retries performed after dispatch.
    /// </summary>
    private const int MaxConcurrencyRetries = 3;

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

        _context.Begin(correlation, initialState, 0);

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

    /// <inheritdoc />
    public Task PrepareDispatchScopeAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default)
    {
        TryAttachScope(envelope);
        return Task.CompletedTask;
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
            for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
            {
                try
                {
                    await PersistActiveScopeAsync(cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (SagaConcurrencyException) when (attempt < MaxConcurrencyRetries - 1)
                {
                    await ReloadActiveScopeAsync(cancellationToken).ConfigureAwait(false);

                    if (!_context.IsActive)
                    {
                        break;
                    }
                }
            }
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
            await _sagaStore.CompleteAsync(
                    SagaCompleteItem.From(_context.Correlation, _context.Version),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Reloads saga state after an optimistic concurrency conflict and re-applies handler mutations.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when the active scope is refreshed.</returns>
    private async Task ReloadActiveScopeAsync(CancellationToken cancellationToken)
    {
        if (_context.Correlation is null)
        {
            return;
        }

        var shadow = _context.CaptureDispatchShadow();
        var stateType = _stateTypeRegistry.ResolveStateType(_context.Correlation.SagaDefinitionId);

        if (stateType is null)
        {
            throw new InvalidOperationException(
                $"Saga definition '{_context.Correlation.SagaDefinitionId}' is not registered.");
        }

        var loaded = await SagaStoreInvoker.LoadAsync(_sagaStore, stateType, _context.Correlation, cancellationToken)
            .ConfigureAwait(false);

        if (loaded?.IsCompleted == true)
        {
            if (shadow.IsDirty)
            {
                throw new SagaConcurrencyException(_context.Correlation);
            }

            _context.Reset();
            return;
        }

        var state = loaded?.State ?? Activator.CreateInstance(stateType)
            ?? throw new InvalidOperationException($"Could not create saga state type '{stateType.FullName}'.");
        var version = loaded?.Version ?? 0;

        _context.Begin(_context.Correlation, state, version);

        if (shadow.IsDirty)
        {
            _context.ReapplyState(shadow.State);
        }

        if (shadow.ShouldComplete)
        {
            _context.Complete();
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
    ///     Re-attaches a dispatch scope created during <see cref="BeforeDispatchAsync" /> for one envelope.
    /// </summary>
    /// <param name="envelope">The processor envelope.</param>
    private void TryAttachScope(IProcessorEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.CorrelationId))
        {
            return;
        }

        var sagaDefinitionId = _stateTypeRegistry.ResolveDefinitionId(envelope.ContractName);

        if (sagaDefinitionId is null)
        {
            return;
        }

        _context.TryAttach(CreateCorrelation(envelope, sagaDefinitionId));
    }
}
