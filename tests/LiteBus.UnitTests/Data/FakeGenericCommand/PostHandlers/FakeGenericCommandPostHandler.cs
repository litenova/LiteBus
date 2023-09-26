using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericCommand.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericCommand.PostHandlers;

public sealed class FakeGenericCommandPostHandler<TPayload> : ICommandPostHandler<FakeGenericCommand<TPayload>, FakeGenericCommandResult>
{
    public Task PostHandleAsync(FakeGenericCommand<TPayload> message, FakeGenericCommandResult messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericCommandPostHandler<TPayload>));
        return Task.CompletedTask;
    }
}