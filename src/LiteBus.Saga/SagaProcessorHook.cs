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
        _sagaStore = sagaStore ?? throw new ArgumentNullException(nameof(sagaStore));
        _stateTypeRegistry = stateTypeRegistry ?? throw new ArgumentNullException(nameof(stateTypeRegistry));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _context = context ?? throw new ArgumentNullException(nameof(context));
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
        var loaded = await SagaStoreInvoker.LoadAsync(_sagaStore, stateType, correlation, cancellationToken)
            .ConfigureAwait(false);

        if (loaded?.IsCompleted == true)
        {
            return;
        }

        var state = loaded?.State ?? Activator.CreateInstance(stateType) ?? throw new InvalidOperationException($"Could not create saga state type '{stateType.FullName}'.");
        var version = loaded?.Version ?? 0;

        _context.Begin(correlation, state, version);
    }

    /// <inheritdoc />
    public async Task AfterDispatchAsync(IProcessorEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

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

        if (_context.IsDirty && _context.ShouldComplete)
        {
            throw new InvalidOperationException(
                "Saga scope cannot call Complete() and SetState() in the same dispatch. Persist final state first, then complete on a later message.");
        }

        if (_context.IsDirty)
        {
            var state = _context.GetState<object>();

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
    ///     Reloads saga state after an optimistic concurrency conflict.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when the active scope is refreshed.</returns>
    private async Task ReloadActiveScopeAsync(CancellationToken cancellationToken)
    {
        if (_context.Correlation is null)
        {
            return;
        }

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
            return;
        }

        var state = loaded?.State ?? Activator.CreateInstance(stateType) ?? throw new InvalidOperationException($"Could not create saga state type '{stateType.FullName}'.");
        var version = loaded?.Version ?? 0;

        _context.Begin(_context.Correlation, state, version);
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
}
