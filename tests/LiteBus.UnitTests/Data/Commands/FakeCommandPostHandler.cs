using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.Commands;

public class FakeCommandPostHandler : ICommandPostHandler<FakeCommand, FakeCommandResult>
{
    public Task PostHandleAsync(IHandleContext<FakeCommand, FakeCommandResult> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeCommandPostHandler));
        return Task.CompletedTask;
    }
}