using LiteBus.Events.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases.ProblematicEvent;

public sealed class ProblematicEventErrorHandler : IEventErrorHandler<ProblematicEvent>
{
    public Task HandleErrorAsync(ProblematicEvent message, object? messageResult, Exception exception, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }
}