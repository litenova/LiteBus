using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericEvent.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericEvent.PreHandlers;

public class FakeGenericEventPreHandler<TPayload> : IEventPreHandler<FakeGenericEvent<TPayload>>
{
    public Task HandleAsync(IHandleContext<FakeGenericEvent<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericEventPreHandler<TPayload>));
        return Task.CompletedTask;
    }
}