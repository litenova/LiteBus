using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.UnitTests.Data.Shared.Events;

namespace LiteBus.UnitTests.Data.Shared.EventGlobalPreHandlers;

public sealed class FakeGlobalEventPreHandler : IEventPreHandler
{
    public object PreHandle(IEvent message)
    {
        (message as FakeParentEvent)!.ExecutedTypes.Add(typeof(FakeGlobalEventPreHandler));
        return Task.CompletedTask;
    }
}