using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericQuery.PostHandlers;

public class FakeSyncGenericQueryPostHandler<TPayload> : ISyncQueryPostHandler<FakeGenericQuery<TPayload>, FakeGenericQueryResult>
{
    public void Handle(IHandleContext<FakeGenericQuery<TPayload>, FakeGenericQueryResult> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeSyncGenericQueryPostHandler<TPayload>));
    }
}