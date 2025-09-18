using LiteBus.Events.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases;

public sealed class GlobalEventPostHandler : IEventPostHandler
{
    public Task PostHandleAsync(IEvent message, object? messageResult, CancellationToken cancellationToken = default)
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