using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases;

/// <summary>
///     Global command error handler used by command module unit tests.
/// </summary>
public class GlobalCommandErrorHandler : ICommandErrorHandler
{
    /// <inheritdoc />
    public Task HandleErrorAsync(
        MessageErrorContext<ICommand, object> context,
        CancellationToken cancellationToken = default)
    {
        if (context.Message is IAuditableCommand auditableCommand)
        {
            auditableCommand.ExecutedTypes.Add(GetType());
        }

        context.Outcome = MessageErrorOutcome.Handled;
        return Task.CompletedTask;
    }
}
