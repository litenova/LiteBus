using LiteBus.MessageModule.UnitTests.Data.FakeGenericQuery.Messages;
using LiteBus.Queries.Abstractions;

namespace LiteBus.MessageModule.UnitTests.Data.FakeGenericQuery.PreHandlers;

public sealed class FakeGenericQueryPreHandler<TPayload> : IQueryPreHandler<FakeGenericQuery<TPayload>>
{
    public Task PreHandleAsync(FakeGenericQuery<TPayload> message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericQueryPreHandler<TPayload>));
        return Task.CompletedTask;
    }
}