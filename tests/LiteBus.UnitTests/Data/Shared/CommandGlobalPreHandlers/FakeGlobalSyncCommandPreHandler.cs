using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.Shared.Commands;

namespace LiteBus.UnitTests.Data.Shared.CommandGlobalPreHandlers;

public class FakeGlobalSyncCommandPreHandler : ISyncCommandPreHandler
{
    public void Handle(IHandleContext<ICommandBase> context)
    {
        (context.Message as FakeParentCommand)!.ExecutedTypes.Add(typeof(FakeGlobalSyncCommandPreHandler));
    }
}