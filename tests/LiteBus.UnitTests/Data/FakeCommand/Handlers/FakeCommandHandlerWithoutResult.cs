using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.FakeCommand.Messages;

namespace LiteBus.UnitTests.Data.FakeCommand.Handlers;

public sealed class FakeCommandHandlerWithoutResult : ICommandHandler<Messages.FakeCommand, FakeCommandResult>
{
    public Task<FakeCommandResult> HandleAsync(Messages.FakeCommand message,
                                               CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeCommandHandlerWithoutResult));
        return Task.FromResult(new FakeCommandResult(message.CorrelationId));
    }
}