using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.Shared.Events;

namespace LiteBus.UnitTests.Data.Shared.EventGlobalPreHandlers;

public class FakeGlobalEventPreHandler : IEventPreHandler
{
    public Task PreHandleAsync(IHandleContext<IEvent> context)
    {
        (context.Message as FakeParentEvent)!.ExecutedTypes.Add(typeof(FakeGlobalEventPreHandler));
        return Task.CompletedTask;
    }
}