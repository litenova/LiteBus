using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericQuery.PostHandlers;

public sealed class FakeGenericQueryPostHandler<TPayload> : IQueryPostHandler<FakeGenericQuery<TPayload>, FakeGenericQueryResult>
{
    public Task PostHandleAsync(FakeGenericQuery<TPayload> message, FakeGenericQueryResult? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericQueryPostHandler<TPayload>));
        return Task.CompletedTask;
    }
}