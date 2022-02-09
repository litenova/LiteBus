using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericEvent.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericEvent.PreHandlers;

public class FakeGenericSyncEventPreHandler<TPayload> : ISyncEventPreHandler<Messages.FakeGenericEvent<TPayload>>
{
    public void Handle(IHandleContext<FakeGenericEvent<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericSyncEventPreHandler<TPayload>));
    }
}