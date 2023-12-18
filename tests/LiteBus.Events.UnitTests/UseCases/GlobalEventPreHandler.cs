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

        if (message is ProblematicEvent.ProblematicEvent problematicEvent && problematicEvent.ThrowExceptionInType == GetType())
        {
            throw new Exception();
        }

        return Task.CompletedTask;
    }
}