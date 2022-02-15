using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.FakeCommand.Messages;

namespace LiteBus.UnitTests.Data.FakeCommand.Handlers;

public class FakeCommandSyncHandler : ISyncCommandHandler<Messages.FakeCommand, FakeCommandResult>
{
    public FakeCommandResult Handle(Messages.FakeCommand message)
    {
        message.ExecutedTypes.Add(typeof(FakeCommandSyncHandler));
        return new FakeCommandResult(message.CorrelationId);
    }
}