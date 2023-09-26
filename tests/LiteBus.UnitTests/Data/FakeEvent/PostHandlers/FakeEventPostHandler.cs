using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;

namespace LiteBus.UnitTests.Data.FakeEvent.PostHandlers;

public sealed class FakeEventPostHandler : IEventPostHandler<FakeEvent.Messages.FakeEvent>
{
    public Task PostHandleAsync(Messages.FakeEvent message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeEventPostHandler));
        return Task.CompletedTask;
    }
}