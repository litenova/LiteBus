using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.Shared.Events;

namespace LiteBus.UnitTests.Data.Shared.EventGlobalPostHandlers;

public class FakeGlobalEventPostHandler : IEventPostHandler
{
    public Task HandleAsync(IHandleContext<IEvent> context)
    {
        (context.Message as FakeParentEvent)!.ExecutedTypes.Add(typeof(FakeGlobalEventPostHandler));
        return Task.CompletedTask;
    }
}