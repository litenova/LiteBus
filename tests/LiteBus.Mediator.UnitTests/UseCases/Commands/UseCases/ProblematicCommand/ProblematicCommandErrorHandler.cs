using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases.ProblematicCommand;

/// <summary>
///     Problematic command error handler used by command module unit tests.
/// </summary>
public sealed class ProblematicCommandErrorHandler : ICommandErrorHandler<ProblematicCommand>
{
    /// <inheritdoc />
    public Task HandleErrorAsync(
        MessageErrorContext<ProblematicCommand, object> context,
        CancellationToken cancellationToken = default)
    {
        context.Message.ExecutedTypes.Add(GetType());

        context.Outcome = MessageErrorOutcome.Handled;
        return Task.CompletedTask;
    }
}
