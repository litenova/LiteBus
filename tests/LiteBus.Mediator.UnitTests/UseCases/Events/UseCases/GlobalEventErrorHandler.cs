using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Mediator.UnitTests.UseCases.Events.UseCases;

/// <summary>
///     Global event error handler used by event module unit tests.
/// </summary>
public class GlobalEventErrorHandler : IEventErrorHandler
{
    /// <inheritdoc />
    public Task HandleErrorAsync(IEvent message, object? messageResult, Exception exception, CancellationToken cancellationToken = default)
    {
        if (message is IAuditableEvent auditableEvent)
        {
            auditableEvent.ExecutedTypes.Add(GetType());
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    object IMessageErrorHandler.HandleError(MessageErrorContext context)
    {
        var typed = context.AsTyped<IEvent, object?>();
        var task = HandleErrorAsync(
            typed.Message,
            typed.MessageResult,
            typed.Exception,
            AmbientExecutionContext.Current.CancellationToken);

        return LegacyErrorHandlerSupport.MarkHandled(context, task);
    }
}
