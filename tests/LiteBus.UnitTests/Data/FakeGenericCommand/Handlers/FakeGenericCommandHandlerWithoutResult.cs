using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericCommand.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericCommand.Handlers;

public sealed class FakeGenericCommandHandlerWithoutResult<TPayload> : ICommandHandler<FakeGenericCommand<TPayload>, FakeGenericCommandResult>
{
    public Task<FakeGenericCommandResult> HandleAsync(FakeGenericCommand<TPayload> message,
                                                      CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericCommandHandlerWithoutResult<TPayload>));
        return Task.FromResult(new FakeGenericCommandResult(message.CorrelationId));
    }
}