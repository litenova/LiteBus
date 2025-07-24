using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases.EventWithTag;

[HandlerTag(Tags.Tag2)]
public sealed class EventWithTagEventHandlerPreHandler2 : IEventPreHandler<EventWithTag>
{
    public Task PreHandleAsync(EventWithTag message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}