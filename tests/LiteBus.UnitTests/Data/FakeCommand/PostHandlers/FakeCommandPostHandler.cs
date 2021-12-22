using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.FakeCommand.Messages;

namespace LiteBus.UnitTests.Data.FakeCommand.PostHandlers;

public class FakeCommandPostHandler : ICommandPostHandler<FakeCommand.Messages.FakeCommand, FakeCommandResult>
{
    public Task PostHandleAsync(IHandleContext<FakeCommand.Messages.FakeCommand, FakeCommandResult> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeCommandPostHandler));
        return Task.CompletedTask;
    }
}