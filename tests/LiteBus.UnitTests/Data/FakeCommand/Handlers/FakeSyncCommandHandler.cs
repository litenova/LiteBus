using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.FakeCommand.Messages;

namespace LiteBus.UnitTests.Data.FakeCommand.Handlers;

public class FakeSyncCommandHandler : ISyncCommandHandler<Messages.FakeCommand, FakeCommandResult>
{
    public FakeCommandResult Handle(Messages.FakeCommand message)
    {
        message.ExecutedTypes.Add(typeof(FakeSyncCommandHandler));
        return new FakeCommandResult(message.CorrelationId);
    }
}