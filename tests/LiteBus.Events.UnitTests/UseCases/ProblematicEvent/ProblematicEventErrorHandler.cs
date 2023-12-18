using LiteBus.Events.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases.ProblematicEvent;

public sealed class ProblematicEventErrorHandler : IEventErrorHandler<ProblematicEvent>
{
    public Task HandleErrorAsync(ProblematicEvent message, object messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }
}