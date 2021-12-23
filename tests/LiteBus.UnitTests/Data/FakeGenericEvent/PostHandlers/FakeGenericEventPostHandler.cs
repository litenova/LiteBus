using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericEvent.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericEvent.PostHandlers;

public class FakeGenericEventPostHandler<TPayload> : IEventPostHandler<FakeGenericEvent<TPayload>>
{
    public Task PostHandleAsync(IHandleContext<FakeGenericEvent<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericEventPostHandler<TPayload>));
        return Task.CompletedTask;
    }
}