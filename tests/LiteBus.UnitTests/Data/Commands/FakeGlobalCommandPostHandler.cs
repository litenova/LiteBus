using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.Commands;

public class FakeGlobalCommandPostHandler : ICommandPostHandler
{
    public Task PostHandleAsync(IHandleContext<ICommandBase> context)
    {
        (context.Message as FakeCommand)!.ExecutedTypes.Add(typeof(FakeGlobalCommandPostHandler));
        return Task.CompletedTask;
    }
}