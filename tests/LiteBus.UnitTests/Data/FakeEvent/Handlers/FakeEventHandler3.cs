using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;

namespace LiteBus.UnitTests.Data.FakeEvent.Handlers;

public class FakeEventHandler3 : IEventHandler<Messages.FakeEvent>
{
    public Task HandleAsync(Messages.FakeEvent message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeEventHandler3));
        return Task.CompletedTask;
    }
}