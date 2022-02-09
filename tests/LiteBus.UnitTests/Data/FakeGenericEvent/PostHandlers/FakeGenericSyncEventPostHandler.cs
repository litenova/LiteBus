using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericEvent.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericEvent.PostHandlers;

public class FakeGenericSyncEventPostHandler<TPayload> : ISyncEventPostHandler<FakeGenericEvent<TPayload>>
{
    public void Handle(IHandleContext<FakeGenericEvent<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericSyncEventPostHandler<TPayload>));
    }
}