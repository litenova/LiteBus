using LiteBus.Events.Abstractions;
using LiteBus.MessageModule.UnitTests.Data.FakeGenericEvent.Messages;

namespace LiteBus.MessageModule.UnitTests.Data.FakeGenericEvent.Handlers;

public sealed class FakeGenericEventHandler3<TPayload> : IEventHandler<FakeGenericEvent<TPayload>>
{
    public Task HandleAsync(FakeGenericEvent<TPayload> message,
                            CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericEventHandler3<TPayload>));
        return Task.CompletedTask;
    }
}