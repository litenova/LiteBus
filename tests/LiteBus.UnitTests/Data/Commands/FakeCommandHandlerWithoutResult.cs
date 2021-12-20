using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.UnitTests.Data.Commands;

public class FakeCommandHandlerWithoutResult : ICommandHandler<FakeCommand, FakeCommandResult>
{
    public Task<FakeCommandResult> HandleAsync(FakeCommand message,
                                               CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeCommandHandlerWithoutResult));
        return Task.FromResult(new FakeCommandResult());
    }
}