using LiteBus.Events.Abstractions;
using LiteBus.MessageModule.UnitTests.Data.FakeGenericEvent.Messages;

namespace LiteBus.MessageModule.UnitTests.Data.FakeGenericEvent.PostHandlers;

public sealed class FakeGenericEventPostHandler<TPayload> : IEventPostHandler<FakeGenericEvent<TPayload>>
{
    public Task PostHandleAsync(FakeGenericEvent<TPayload> message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericEventPostHandler<TPayload>));
        return Task.CompletedTask;
    }
}