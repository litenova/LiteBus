using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases.EventWithTag;

[HandlerTag(Tags.Tag1)]
public sealed class EventWithTagEventHandlerPreHandler1 : IEventPreHandler<EventWithTag>
{
    public Task PreHandleAsync(EventWithTag message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}