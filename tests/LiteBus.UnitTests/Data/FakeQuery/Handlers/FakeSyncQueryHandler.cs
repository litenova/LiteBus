using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.FakeQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeQuery.Handlers;

public class FakeSyncQueryHandler : ISyncQueryHandler<Messages.FakeQuery, FakeQueryResult>
{
    public FakeQueryResult Handle(Messages.FakeQuery message)
    {
        message.ExecutedTypes.Add(typeof(FakeSyncQueryHandler));
        return new FakeQueryResult(message.CorrelationId);
    }
}