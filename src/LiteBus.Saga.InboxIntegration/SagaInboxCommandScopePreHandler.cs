using LiteBus.Commands.Abstractions;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;

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
    ///     Initializes a new instance of the <see cref="SagaInboxCommandScopePreHandler" /> class.
    /// </summary>
    /// <param name="context">The ambient saga context exposed to handlers.</param>
    public SagaInboxCommandScopePreHandler(SagaExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
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

        if (!items.TryGetValue(InboxExecutionContextKeys.MessageId, out var messageIdValue)
            || messageIdValue is not Guid messageId)
        {
            return Task.CompletedTask;
        }

        _context.TryAttach(messageId);

        return Task.CompletedTask;
    }
}
