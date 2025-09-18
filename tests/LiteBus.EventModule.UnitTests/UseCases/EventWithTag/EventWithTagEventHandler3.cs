using LiteBus.Events.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases.EventWithTag;

public sealed class EventWithTagEventHandler3 : IEventHandler<EventWithTag>
{
    public Task HandleAsync(EventWithTag message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }
}