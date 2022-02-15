using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.FakeCommand.Messages;

namespace LiteBus.UnitTests.Data.FakeCommand.PostHandlers;

public class FakeCommandSyncPostHandler : ISyncCommandPostHandler<Messages.FakeCommand, FakeCommandResult>
{
    public void Handle(IHandleContext<Messages.FakeCommand, FakeCommandResult> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeCommandSyncPostHandler));
    }
}