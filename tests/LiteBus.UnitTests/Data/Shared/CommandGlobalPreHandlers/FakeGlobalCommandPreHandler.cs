using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.Shared.Commands;

namespace LiteBus.UnitTests.Data.Shared.CommandGlobalPreHandlers;

public class FakeGlobalCommandPreHandler : ICommandPreHandler
{
    public Task PreHandleAsync(IHandleContext<ICommandBase> context)
    {
        (context.Message as FakeParentCommand)!.ExecutedTypes.Add(typeof(FakeGlobalCommandPreHandler));
        return Task.CompletedTask;
    }
}