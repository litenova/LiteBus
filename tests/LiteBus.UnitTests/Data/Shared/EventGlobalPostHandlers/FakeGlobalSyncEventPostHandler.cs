using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.Shared.Events;

namespace LiteBus.UnitTests.Data.Shared.EventGlobalPostHandlers;

public class FakeGlobalSyncEventPostHandler : ISyncEventPostHandler
{
    public void Handle(IHandleContext<IEvent> context)
    {
        (context.Message as FakeParentEvent)!.ExecutedTypes.Add(typeof(FakeGlobalSyncEventPostHandler));
    }
}