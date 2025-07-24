using LiteBus.MessageModule.UnitTests.Data.FakeQuery.Messages;
using LiteBus.Queries.Abstractions;

namespace LiteBus.MessageModule.UnitTests.Data.FakeQuery.Handlers;

public sealed class FakeQueryHandlerWithoutResult : IQueryHandler<Messages.FakeQuery, FakeQueryResult>
{
    public Task<FakeQueryResult> HandleAsync(Messages.FakeQuery message,
                                             CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeQueryHandlerWithoutResult));
        return Task.FromResult(new FakeQueryResult(message.CorrelationId));
    }
}