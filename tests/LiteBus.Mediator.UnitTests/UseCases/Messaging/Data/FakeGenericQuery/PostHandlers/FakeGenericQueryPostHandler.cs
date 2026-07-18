using LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeGenericQuery.Messages;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeGenericQuery.PostHandlers;

public sealed class FakeGenericQueryPostHandler<TPayload> : IQueryPostHandler<FakeGenericQuery<TPayload>, FakeGenericQueryResult>
{
    public Task PostHandleAsync(FakeGenericQuery<TPayload> message, FakeGenericQueryResult? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericQueryPostHandler<TPayload>));
        return Task.CompletedTask;
    }
}