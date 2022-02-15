using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.Shared.Commands;

namespace LiteBus.UnitTests.Data.Shared.CommandGlobalPostHandlers;

public class FakeGlobalCommandSyncPostHandler : ISyncCommandPostHandler
{
    public void Handle(IHandleContext<ICommand> context)
    {
        (context.Message as FakeParentCommand)!.ExecutedTypes.Add(typeof(FakeGlobalCommandSyncPostHandler));
    }
}