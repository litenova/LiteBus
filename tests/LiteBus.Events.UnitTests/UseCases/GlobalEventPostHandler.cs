using LiteBus.Events.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases;

public sealed class GlobalEventPostHandler : IEventPostHandler
{
    public Task PostHandleAsync(IEvent message, object messageResult, CancellationToken cancellationToken = default)
    {
        if (message is IAuditableEvent auditableEvent)
        {
            auditableEvent.ExecutedTypes.Add(GetType());
        }

        return Task.CompletedTask;
    }
}