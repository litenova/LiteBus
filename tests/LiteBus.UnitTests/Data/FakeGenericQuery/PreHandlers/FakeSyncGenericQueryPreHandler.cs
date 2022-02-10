using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericQuery.PreHandlers;

public class FakeSyncGenericQueryPreHandler<TPayload> : ISyncQueryPreHandler<FakeGenericQuery<TPayload>>
{
    public void Handle(IHandleContext<FakeGenericQuery<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeSyncGenericQueryPreHandler<TPayload>));
    }
}