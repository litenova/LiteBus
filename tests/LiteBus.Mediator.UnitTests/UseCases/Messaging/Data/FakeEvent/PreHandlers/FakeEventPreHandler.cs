using LiteBus.Events.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeEvent.PreStageHandlers;

public sealed class FakeEventPreHandler : IEventPreHandler<Messages.FakeEvent>
{
    public Task PreHandleAsync(Messages.FakeEvent message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeEventPreHandler));
        return Task.CompletedTask;
    }
}