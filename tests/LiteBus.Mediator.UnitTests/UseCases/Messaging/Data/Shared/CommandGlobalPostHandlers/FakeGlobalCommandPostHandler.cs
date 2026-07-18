using LiteBus.Commands.Abstractions;
using LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.Shared.Commands;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.Shared.CommandGlobalPostHandlers;

public sealed class FakeGlobalCommandPostHandler : ICommandPostHandler
{
    public Task PostHandleAsync(ICommand message, object? messageResult, CancellationToken cancellationToken = default)
    {
        (message as FakeParentCommand)!.ExecutedTypes.Add(typeof(FakeGlobalCommandPostHandler));
        return Task.CompletedTask;
    }
}