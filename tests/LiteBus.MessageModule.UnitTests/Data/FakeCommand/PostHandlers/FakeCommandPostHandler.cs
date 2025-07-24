using LiteBus.Commands.Abstractions;
using LiteBus.MessageModule.UnitTests.Data.FakeCommand.Messages;

namespace LiteBus.MessageModule.UnitTests.Data.FakeCommand.PostHandlers;

public sealed class FakeCommandPostHandler : ICommandPostHandler<Messages.FakeCommand, FakeCommandResult>
{
    public Task PostHandleAsync(Messages.FakeCommand message, FakeCommandResult? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeCommandPostHandler));
        return Task.CompletedTask;
    }
}