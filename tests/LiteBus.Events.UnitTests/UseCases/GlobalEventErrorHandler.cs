using LiteBus.Events.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases;

public class GlobalEventErrorHandler : IEventErrorHandler
{
    public Task HandleErrorAsync(IEvent message, object? messageResult, Exception exception, CancellationToken cancellationToken = default)
    {
        if (message is IAuditableEvent auditableEvent)
        {
            auditableEvent.ExecutedTypes.Add(GetType());
        }

        return Task.CompletedTask;
    }
}