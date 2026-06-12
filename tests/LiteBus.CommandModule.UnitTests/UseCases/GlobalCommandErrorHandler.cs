using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;

namespace LiteBus.CommandModule.UnitTests.UseCases;

/// <summary>
///     Global command error handler used by command module unit tests.
/// </summary>
public class GlobalCommandErrorHandler : ICommandErrorHandler
{
    /// <inheritdoc />
    public Task HandleErrorAsync(ICommand message, object? messageResult, Exception exception, CancellationToken cancellationToken = default)
    {
        if (message is IAuditableCommand auditableCommand)
        {
            auditableCommand.ExecutedTypes.Add(GetType());
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    object IMessageErrorHandler.HandleError(MessageErrorContext context)
    {
        var typed = context.AsTyped<ICommand, object?>();
        var task = HandleErrorAsync(
            typed.Message,
            typed.MessageResult,
            typed.Exception,
            AmbientExecutionContext.Current.CancellationToken);

        return LegacyErrorHandlerSupport.MarkHandled(context, task);
    }
}
