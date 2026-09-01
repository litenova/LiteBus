using LiteBus.Events.Abstractions;
using LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeGenericEvent.Messages;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeGenericEvent.PreStageHandlers;

public sealed class FakeGenericEventPreHandler<TPayload> : IEventPreHandler<FakeGenericEvent<TPayload>>
{
    public Task PreHandleAsync(FakeGenericEvent<TPayload> message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericEventPreHandler<TPayload>));
        return Task.CompletedTask;
    }
}