using LiteBus.Events.Abstractions;
using LiteBus.UnitTests.Data.Shared.Events;

namespace LiteBus.UnitTests.Data.Shared.EventGlobalPreHandlers;

public sealed class FakeGlobalEventPreHandler : IEventPreHandler
{
    public Task PreHandleAsync(IEvent message, CancellationToken cancellationToken = default)
    {
        (message as FakeParentEvent)!.ExecutedTypes.Add(typeof(FakeGlobalEventPreHandler));
        return Task.CompletedTask;
    }
}