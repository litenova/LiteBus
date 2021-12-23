using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.Shared.Commands;

namespace LiteBus.UnitTests.Data.Shared.CommandGlobalPostHandlers;

public class FakeGlobalCommandPostHandler : ICommandPostHandler
{
    public Task PostHandleAsync(IHandleContext<ICommandBase> context)
    {
        (context.Message as FakeParentCommand)!.ExecutedTypes.Add(typeof(FakeGlobalCommandPostHandler));
        return Task.CompletedTask;
    }
}