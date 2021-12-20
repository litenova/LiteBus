using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.Commands;

public class FakeGlobalCommandPreHandler : ICommandPreHandler
{
    public Task PreHandleAsync(IHandleContext<ICommandBase> context)
    {
        (context.Message as FakeCommand)!.ExecutedTypes.Add(typeof(FakeGlobalCommandPreHandler));
        return Task.CompletedTask;
    }
}