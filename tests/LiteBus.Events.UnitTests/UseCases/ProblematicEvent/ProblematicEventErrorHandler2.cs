using LiteBus.Events.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases.ProblematicEvent;

public sealed class ProblematicEventErrorHandler2 : IEventErrorHandler<ProblematicEvent>
{
    public Task HandleErrorAsync(ProblematicEvent message, object? messageResult, Exception exception, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }
}