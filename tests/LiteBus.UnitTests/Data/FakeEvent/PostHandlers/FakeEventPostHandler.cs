using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeEvent.PostHandlers;

public class FakeEventPostHandler : IEventPostHandler<FakeEvent.Messages.FakeEvent>
{
    public Task PostHandleAsync(IHandleContext<FakeEvent.Messages.FakeEvent> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeEventPostHandler));
        return Task.CompletedTask;
    }
}