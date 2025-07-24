using LiteBus.Events.Abstractions;
using LiteBus.MessageModule.UnitTests.Data.Shared.Events;

namespace LiteBus.MessageModule.UnitTests.Data.Shared.EventGlobalPostHandlers;

public sealed class FakeGlobalEventPostHandler : IEventPostHandler
{
    public Task PostHandleAsync(IEvent message, object? messageResult, CancellationToken cancellationToken = default)
    {
        (message as FakeParentEvent)!.ExecutedTypes.Add(typeof(FakeGlobalEventPostHandler));
        return Task.CompletedTask;
    }
}