using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.Shared.Commands;

namespace LiteBus.UnitTests.Data.Shared.CommandGlobalPostHandlers;

public sealed class FakeGlobalCommandPostHandler : ICommandPostHandler
{
    public Task PostHandleAsync(ICommand message, CancellationToken cancellationToken = default)
    {
        (message as FakeParentCommand)!.ExecutedTypes.Add(typeof(FakeGlobalCommandPostHandler));
        return Task.CompletedTask;
    }
}