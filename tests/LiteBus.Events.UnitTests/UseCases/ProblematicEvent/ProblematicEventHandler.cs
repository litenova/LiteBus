using LiteBus.Events.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases.ProblematicEvent;

public sealed class ProblematicEventHandler : IEventHandler<ProblematicEvent>
{
    public Task HandleAsync(ProblematicEvent message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        if (message.ThrowExceptionInType == GetType())
        {
            throw new Exception();
        }

        return Task.CompletedTask;
    }
}