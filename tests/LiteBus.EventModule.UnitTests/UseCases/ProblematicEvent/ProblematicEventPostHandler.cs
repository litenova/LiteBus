using LiteBus.Events.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases.ProblematicEvent;

public sealed class ProblematicEventPostHandler : IEventPostHandler<ProblematicEvent>
{
    public Task PostHandleAsync(ProblematicEvent message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        if (message.ThrowExceptionInType == GetType())
        {
            throw new Exception();
        }

        return Task.CompletedTask;
    }
}