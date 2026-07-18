using LiteBus.Commands.Abstractions;
using LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeCommand.Messages;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeCommand.PostHandlers;

public sealed class FakeCommandPostHandler : ICommandPostHandler<Messages.FakeCommand, FakeCommandResult>
{
    public Task PostHandleAsync(Messages.FakeCommand message, FakeCommandResult? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeCommandPostHandler));
        return Task.CompletedTask;
    }
}