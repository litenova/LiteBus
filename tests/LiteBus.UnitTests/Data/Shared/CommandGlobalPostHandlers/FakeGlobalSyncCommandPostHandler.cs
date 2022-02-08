using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.Shared.Commands;

namespace LiteBus.UnitTests.Data.Shared.CommandGlobalPostHandlers;

public class FakeGlobalSyncCommandPostHandler : ISyncCommandPostHandler
{
    public void Handle(IHandleContext<ICommandBase> context)
    {
        (context.Message as FakeParentCommand)!.ExecutedTypes.Add(typeof(FakeGlobalSyncCommandPostHandler));
    }
}