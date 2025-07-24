using LiteBus.Events.Abstractions;

namespace LiteBus.MessageModule.UnitTests.Data.FakeEvent.Handlers;

public sealed class FakeEventHandler3 : IEventHandler<Messages.FakeEvent>
{
    public Task HandleAsync(Messages.FakeEvent message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeEventHandler3));
        return Task.CompletedTask;
    }
}