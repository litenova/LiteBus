using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases.EventWithTag;

[HandlerOrder(2)]
[HandlerTag(Tags.Tag2)]
public sealed class EventWithTagEventHandlerPostHandler2 : IEventPostHandler<EventWithTag>
{
    public Task PostHandleAsync(EventWithTag message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}