using LiteBus.Events.Abstractions;

namespace LiteBus.UnitTests.Data.FakeEvent.Handlers;

public class FakeSyncEventHandler1 : ISyncEventHandler<Messages.FakeEvent>
{
    public void Handle(Messages.FakeEvent message)
    {
        message.ExecutedTypes.Add(typeof(FakeSyncEventHandler1));
    }
}