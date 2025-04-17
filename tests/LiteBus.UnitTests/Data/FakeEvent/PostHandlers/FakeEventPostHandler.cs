using LiteBus.Events.Abstractions;

namespace LiteBus.UnitTests.Data.FakeEvent.PostHandlers;

public sealed class FakeEventPostHandler : IEventPostHandler<Messages.FakeEvent>
{
    public Task PostHandleAsync(Messages.FakeEvent message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeEventPostHandler));
        return Task.CompletedTask;
    }
}