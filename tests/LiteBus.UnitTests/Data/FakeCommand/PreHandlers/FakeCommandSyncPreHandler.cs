using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeCommand.PreHandlers;

public class FakeCommandSyncPreHandler : ISyncCommandPreHandler<FakeCommand.Messages.FakeCommand>
{
    public void Handle(IHandleContext<Messages.FakeCommand> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeCommandSyncPreHandler));
    }
}