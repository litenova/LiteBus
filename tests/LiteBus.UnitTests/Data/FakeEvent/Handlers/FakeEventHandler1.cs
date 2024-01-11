using LiteBus.Events.Abstractions;

namespace LiteBus.UnitTests.Data.FakeEvent.Handlers;

public sealed class FakeEventHandler1 : IEventHandler<Messages.FakeEvent>
{
    public Task HandleAsync(Messages.FakeEvent message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeEventHandler1));
        return Task.CompletedTask;
    }
}