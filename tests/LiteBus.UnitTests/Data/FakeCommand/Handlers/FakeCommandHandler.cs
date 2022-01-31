using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.FakeCommand.Messages;

namespace LiteBus.UnitTests.Data.FakeCommand.Handlers;

public class FakeCommandHandler : ICommandHandler<Messages.FakeCommand, FakeCommandResult>
{
    public Task<FakeCommandResult> HandleAsync(Messages.FakeCommand message,
                                               CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeCommandHandler));
        return Task.FromResult(new FakeCommandResult(message.CorrelationId));
    }
}

public class FakeSyncCommandHandler : ISyncCommandHandler<Messages.FakeCommand, FakeCommandResult>
{
    public FakeCommandResult Handle(Messages.FakeCommand message)
    {
        message.ExecutedTypes.Add(typeof(FakeSyncCommandHandler));
        return new FakeCommandResult(message.CorrelationId);
    }
}