using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericQuery.PreHandlers;

public sealed class FakeGenericQueryPreHandler<TPayload> : IQueryPreHandler<Messages.FakeGenericQuery<TPayload>>
{
    public Task PreHandleAsync(FakeGenericQuery<TPayload> message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericQueryPreHandler<TPayload>));
        return Task.CompletedTask;
    }
}