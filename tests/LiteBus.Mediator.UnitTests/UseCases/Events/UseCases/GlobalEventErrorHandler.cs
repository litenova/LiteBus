using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Events.UseCases;

/// <summary>
///     Global event error handler used by event module unit tests.
/// </summary>
public class GlobalEventErrorHandler : IEventErrorHandler
{
    /// <inheritdoc />
    public Task HandleErrorAsync(
        MessageErrorContext<IEvent, object> context,
        CancellationToken cancellationToken = default)
    {
        if (context.Message is IAuditableEvent auditableEvent)
        {
            auditableEvent.ExecutedTypes.Add(GetType());
        }

        context.Outcome = MessageErrorOutcome.Handled;
        return Task.CompletedTask;
    }
}
