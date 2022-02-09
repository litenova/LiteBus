using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeEvent.PostHandlers;

public class FakeSyncEventPostHandler : ISyncEventPostHandler<FakeEvent.Messages.FakeEvent>
{
    public void Handle(IHandleContext<Messages.FakeEvent> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeSyncEventPostHandler));
    }
}