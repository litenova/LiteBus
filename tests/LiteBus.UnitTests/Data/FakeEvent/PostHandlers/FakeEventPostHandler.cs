using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeEvent.PostHandlers;

public class FakeEventPostHandler : IEventPostHandler<Messages.FakeEvent>
{
    public Task HandleAsync(IHandleContext<Messages.FakeEvent> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeEventPostHandler));
        return Task.CompletedTask;
    }
}