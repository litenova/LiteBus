using LiteBus.Events.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericEvent.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericEvent.Handlers;

public class FakeGenericSyncEventHandler2<TPayload> : ISyncEventHandler<FakeGenericEvent<TPayload>>
{
    public void Handle(FakeGenericEvent<TPayload> message)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericSyncEventHandler2<TPayload>));
    }
}