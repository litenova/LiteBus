using LiteBus.Events.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases;

public sealed class GlobalEventPreHandler : IEventPreHandler
{
    public Task PreHandleAsync(IEvent message, CancellationToken cancellationToken = default)
    {
        if (message is IAuditableEvent auditableEvent)
        {
            auditableEvent.ExecutedTypes.Add(GetType());
        }

        return Task.CompletedTask;
    }
}