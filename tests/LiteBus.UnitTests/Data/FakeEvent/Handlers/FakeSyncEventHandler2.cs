using LiteBus.Events.Abstractions;

namespace LiteBus.UnitTests.Data.FakeEvent.Handlers;

public class FakeSyncEventHandler2 : ISyncEventHandler<Messages.FakeEvent>
{
    public void Handle(Messages.FakeEvent message)
    {
        message.ExecutedTypes.Add(typeof(FakeSyncEventHandler2));
    }
}