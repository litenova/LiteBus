using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.UnitTests.UseCases.EventWithTag;

[HandlerTags(Tags.Tag1, Tags.Tag2)]
public sealed class EventWithTagEventHandler4 : IEventHandler<EventWithTag>
{
    public Task HandleAsync(EventWithTag message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }
}