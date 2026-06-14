using LiteBus.Commands.Abstractions;
using LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeCommand.Messages;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeCommand.Handlers;

public sealed class FakeCommandHandlerWithoutResult : ICommandHandler<Messages.FakeCommand, FakeCommandResult>
{
    public Task<FakeCommandResult> HandleAsync(Messages.FakeCommand message,
                                               CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeCommandHandlerWithoutResult));
        return Task.FromResult(new FakeCommandResult(message.CorrelationId));
    }
}