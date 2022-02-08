using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeGenericEvent.PreHandlers;

public class FakeGenericEventPreHandler<TPayload> : IEventPreHandler<Messages.FakeGenericEvent<TPayload>>
{
    public Task HandleAsync(IHandleContext<Messages.FakeGenericEvent<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericEventPreHandler<TPayload>));
        return Task.CompletedTask;
    }
}