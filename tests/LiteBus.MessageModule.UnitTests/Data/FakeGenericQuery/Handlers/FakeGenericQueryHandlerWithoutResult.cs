using LiteBus.MessageModule.UnitTests.Data.FakeGenericQuery.Messages;
using LiteBus.Queries.Abstractions;

namespace LiteBus.MessageModule.UnitTests.Data.FakeGenericQuery.Handlers;

public sealed class FakeGenericQueryHandlerWithoutResult<TPayload> : IQueryHandler<FakeGenericQuery<TPayload>, FakeGenericQueryResult>
{
    public Task<FakeGenericQueryResult> HandleAsync(FakeGenericQuery<TPayload> message,
                                                    CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericQueryHandlerWithoutResult<TPayload>));
        return Task.FromResult(new FakeGenericQueryResult(message.CorrelationId));
    }
}