using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.Shared.Commands;

namespace LiteBus.UnitTests.Data.Shared.CommandGlobalPreHandlers;

public sealed class FakeGlobalCommandPreHandler : ICommandPreHandler
{
    public Task PreHandleAsync(ICommand message, CancellationToken cancellationToken = default)
    {
        (message as FakeParentCommand)!.ExecutedTypes.Add(typeof(FakeGlobalCommandPreHandler));
        return Task.CompletedTask;
    }
}