using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.EventModule.UnitTests.UseCases.EventWithTag;

[HandlerPriority(1)]
[HandlerTag(Tags.Tag1)]
public sealed class EventWithTagEventHandlerPostHandler1 : IEventPostHandler<EventWithTag>
{
    public Task PostHandleAsync(EventWithTag message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}