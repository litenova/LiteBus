using LiteBus.Events.Abstractions;

namespace LiteBus.UnitTests.Data.FakeEvent.Handlers;

public class FakeSyncEventHandler3 : ISyncEventHandler<Messages.FakeEvent>
{
    public void Handle(Messages.FakeEvent message)
    {
        message.ExecutedTypes.Add(typeof(FakeSyncEventHandler3));
    }
}