using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericCommand.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericCommand.PreHandlers;

public sealed class FakeGenericCommandPreHandler<TPayload> : ICommandPreHandler<FakeGenericCommand<TPayload>>
{
    public Task PreHandleAsync(FakeGenericCommand<TPayload> message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericCommandPreHandler<TPayload>));
        return Task.CompletedTask;
    }
}