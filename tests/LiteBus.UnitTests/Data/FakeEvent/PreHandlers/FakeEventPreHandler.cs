using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeEvent.PreHandlers;

public class FakeEventPreHandler : IEventPreHandler<Messages.FakeEvent>
{
    public Task HandleAsync(IHandleContext<Messages.FakeEvent> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeEventPreHandler));
        return Task.CompletedTask;
    }
}