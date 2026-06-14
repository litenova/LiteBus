using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases.ProblematicCommand;

/// <summary>
///     Second problematic command error handler used by command module unit tests.
/// </summary>
public sealed class ProblematicCommandErrorHandler2 : ICommandErrorHandler<ProblematicCommand>
{
    /// <inheritdoc />
    public Task HandleErrorAsync(ProblematicCommand message, object? messageResult, Exception exception, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    object IMessageErrorHandler.HandleError(MessageErrorContext context)
    {
        var typed = context.AsTyped<ProblematicCommand, object?>();
        var task = HandleErrorAsync(
            typed.Message,
            typed.MessageResult,
            typed.Exception,
            AmbientExecutionContext.Current.CancellationToken);

        return LegacyErrorHandlerSupport.MarkHandled(context, task);
    }
}
