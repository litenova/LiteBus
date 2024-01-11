using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.Shared.Queries;

namespace LiteBus.UnitTests.Data.Shared.QueryGlobalPreHandlers;

public sealed class FakeGlobalQueryPreHandler : IQueryPreHandler
{
    public Task PreHandleAsync(IQuery message, CancellationToken cancellationToken = default)
    {
        (message as FakeParentQuery)!.ExecutedTypes.Add(typeof(FakeGlobalQueryPreHandler));
        return Task.CompletedTask;
    }
}