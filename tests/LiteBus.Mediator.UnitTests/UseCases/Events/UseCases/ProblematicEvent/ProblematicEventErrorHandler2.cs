using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Mediator.UnitTests.UseCases.Events.UseCases.ProblematicEvent;

/// <summary>
///     Second problematic event error handler used by event module unit tests.
/// </summary>
public sealed class ProblematicEventErrorHandler2 : IEventErrorHandler<ProblematicEvent>
{
    /// <inheritdoc />
    public Task HandleErrorAsync(ProblematicEvent message, object? messageResult, Exception exception, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    object IMessageErrorHandler.HandleError(MessageErrorContext context)
    {
        var typed = context.AsTyped<ProblematicEvent, object?>();
        var task = HandleErrorAsync(
            typed.Message,
            typed.MessageResult,
            typed.Exception,
            AmbientExecutionContext.Current.CancellationToken);

        return LegacyErrorHandlerSupport.MarkHandled(context, task);
    }
}
