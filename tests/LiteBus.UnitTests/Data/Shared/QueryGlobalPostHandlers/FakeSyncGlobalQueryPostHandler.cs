using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.Shared.Queries;

namespace LiteBus.UnitTests.Data.Shared.QueryGlobalPostHandlers;

public class FakeSyncGlobalQueryPostHandler : ISyncQueryPostHandler
{
    public void Handle(IHandleContext<IQueryBase> context)
    {
        (context.Message as FakeParentQuery)!.ExecutedTypes.Add(typeof(FakeSyncGlobalQueryPostHandler));
    }
}