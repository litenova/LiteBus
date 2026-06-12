using LiteBus.Commands.Abstractions;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga.InboxIntegration;

/// <summary>
///     Re-attaches inbox saga scope before command handlers run through in-process dispatch.
/// </summary>
internal sealed class SagaInboxCommandScopePreHandler : ICommandPreHandler
{
    /// <summary>
    ///     Gets the ambient saga context exposed to handlers.
    /// </summary>
    private readonly SagaExecutionContext _context;

    /// <summary>
    ///     Gets the registry that maps saga definition identifiers to state types.
    /// </summary>
    private readonly ISagaStateTypeRegistry _stateTypeRegistry;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SagaInboxCommandScopePreHandler" /> class.
    /// </summary>
    /// <param name="context">The ambient saga context exposed to handlers.</param>
    /// <param name="stateTypeRegistry">The registry that maps saga definition identifiers to state types.</param>
    public SagaInboxCommandScopePreHandler(
        SagaExecutionContext context,
        ISagaStateTypeRegistry stateTypeRegistry)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(stateTypeRegistry);
        _context = context;
        _stateTypeRegistry = stateTypeRegistry;
    }

    /// <inheritdoc />
    public Task PreHandleAsync(ICommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!AmbientExecutionContext.HasCurrent)
        {
            return Task.CompletedTask;
        }

        var items = AmbientExecutionContext.Current.Items;

        if (!items.TryGetValue(InboxExecutionContextKeys.IsInboxExecution, out var inboxExecution)
            || inboxExecution is not true)
        {
            return Task.CompletedTask;
        }

        if (!items.TryGetValue(MessageTraceContextKeys.CorrelationId, out var correlationValue)
            || correlationValue is not string correlationId
            || string.IsNullOrWhiteSpace(correlationId))
        {
            return Task.CompletedTask;
        }

        if (!items.TryGetValue(InboxExecutionContextKeys.ContractName, out var contractValue)
            || contractValue is not string contractName)
        {
            return Task.CompletedTask;
        }

        var sagaDefinitionId = _stateTypeRegistry.ResolveDefinitionId(contractName);

        if (sagaDefinitionId is null)
        {
            return Task.CompletedTask;
        }

        items.TryGetValue(MessageTraceContextKeys.TenantId, out var tenantValue);

        _context.TryAttach(new SagaCorrelation
        {
            CorrelationId = correlationId,
            SagaDefinitionId = sagaDefinitionId,
            TenantId = tenantValue as string
        });

        return Task.CompletedTask;
    }
}
