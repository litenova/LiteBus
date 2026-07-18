using LiteBus.Events.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Events.UseCases.ProblematicEvent;

public sealed class ProblematicEventPreHandler : IEventPreHandler<ProblematicEvent>
{
    public Task PreHandleAsync(ProblematicEvent message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        if (message.ThrowExceptionInType == GetType())
        {
            throw new Exception();
        }

        return Task.CompletedTask;
    }
}