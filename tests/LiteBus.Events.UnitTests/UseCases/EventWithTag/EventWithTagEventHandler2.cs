using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases.EventWithTag;

[HandlerTag(Tags.Tag2)]
public sealed class EventWithTagEventHandler2 : IEventHandler<EventWithTag>
{
    public Task HandleAsync(EventWithTag message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }
}