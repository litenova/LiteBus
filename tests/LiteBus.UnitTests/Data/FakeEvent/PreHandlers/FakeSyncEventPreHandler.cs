using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeEvent.PreHandlers;

public class FakeSyncEventPreHandler : ISyncEventPreHandler<FakeEvent.Messages.FakeEvent>
{
    public void Handle(IHandleContext<Messages.FakeEvent> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeSyncEventPreHandler));
    }
}