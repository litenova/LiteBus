using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Events.UseCases.ProblematicEvent;

/// <summary>
///     Second problematic event error handler used by event module unit tests.
/// </summary>
public sealed class ProblematicEventErrorHandler2 : IEventErrorHandler<ProblematicEvent>
{
    /// <inheritdoc />
    public Task HandleErrorAsync(
        MessageErrorContext<ProblematicEvent, object> context,
        CancellationToken cancellationToken = default)
    {
        context.Message.ExecutedTypes.Add(GetType());

        context.Outcome = MessageErrorOutcome.Handled;
        return Task.CompletedTask;
    }
}
