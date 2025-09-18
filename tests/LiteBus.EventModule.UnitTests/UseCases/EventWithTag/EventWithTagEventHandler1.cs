using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases.EventWithTag;

[HandlerTag(Tags.Tag1)]
public sealed class EventWithTagEventHandler1 : IFilteredEventHandler, IEventHandler<EventWithTag>
{
    public Task HandleAsync(EventWithTag message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }
}