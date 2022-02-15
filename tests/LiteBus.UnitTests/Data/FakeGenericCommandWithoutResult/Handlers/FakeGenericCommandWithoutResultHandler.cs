using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericCommandWithoutResult.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericCommandWithoutResult.Handlers;

public class
    FakeGenericCommandWithoutResultHandler<TPayload> : ICommandHandler<FakeGenericCommandWithoutResult<TPayload>>
{
    public Task HandleAsync(FakeGenericCommandWithoutResult<TPayload> message,
                            CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericCommandWithoutResultHandler<TPayload>));
        return Task.CompletedTask;
    }
}