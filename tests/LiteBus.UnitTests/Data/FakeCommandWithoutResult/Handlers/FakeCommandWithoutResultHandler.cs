using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.FakeCommandWithoutResult.Messages;

namespace LiteBus.UnitTests.Data.FakeCommandWithoutResult.Handlers;

public class FakeCommandWithoutResultCommandHandler : ICommandHandler<Messages.FakeCommandWithoutResult>
{
    public Task HandleAsync(Messages.FakeCommandWithoutResult message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeCommandWithoutResultCommandHandler));
        return Task.CompletedTask;
    }
}