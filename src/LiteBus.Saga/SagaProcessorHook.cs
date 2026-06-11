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
    ///     Gets the registry that maps saga type names to state types.
    /// </summary>
    private readonly ISagaStateTypeRegistry _stateTypeRegistry;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SagaProcessorHook" /> class.
    /// </summary>
    /// <param name="sagaStore">The durable saga store.</param>
    /// <param name="stateTypeRegistry">The registry that maps saga type names to state types.</param>
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

        var stateType = _stateTypeRegistry.Resolve(envelope.ContractName);

        if (stateType is null)
        {
            return;
        }

        var correlation = new SagaCorrelation
        {
            CorrelationId = envelope.CorrelationId,
            SagaType = envelope.ContractName
        };

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
            if (_context.ShouldComplete)
            {
                await _sagaStore.CompleteAsync(_context.Correlation, cancellationToken).ConfigureAwait(false);
                return;
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
            }
        }
        finally
        {
            _context.Reset();
        }
    }
}