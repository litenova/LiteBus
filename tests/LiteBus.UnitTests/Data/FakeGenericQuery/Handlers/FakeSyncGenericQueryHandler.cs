using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericQuery.Handlers;

public class FakeSyncGenericQueryHandler<TPayload> : ISyncQueryHandler<FakeGenericQuery<TPayload>, FakeGenericQueryResult>
{
    public FakeGenericQueryResult Handle(FakeGenericQuery<TPayload> message)
    {
        message.ExecutedTypes.Add(typeof(FakeSyncGenericQueryHandler<TPayload>));
        return new FakeGenericQueryResult(message.CorrelationId);
    }
}