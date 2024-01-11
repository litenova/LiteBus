using LiteBus.Events.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericEvent.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericEvent.PreHandlers;

public sealed class FakeGenericEventPreHandler<TPayload> : IEventPreHandler<FakeGenericEvent<TPayload>>
{
    public Task PreHandleAsync(FakeGenericEvent<TPayload> message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericEventPreHandler<TPayload>));
        return Task.CompletedTask;
    }
}