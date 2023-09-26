using System.Threading;
using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericQuery.Handlers;

public sealed class FakeGenericQueryHandlerWithoutResult<TPayload> : IQueryHandler<FakeGenericQuery<TPayload>, FakeGenericQueryResult>
{
    public Task<FakeGenericQueryResult> HandleAsync(FakeGenericQuery<TPayload> message,
                                               CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericQueryHandlerWithoutResult<TPayload>));
        return Task.FromResult(new FakeGenericQueryResult(message.CorrelationId));
    }
}