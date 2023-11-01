using System.Threading;
using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.Shared.Queries;

namespace LiteBus.UnitTests.Data.Shared.QueryGlobalPostHandlers;

public sealed class FakeGlobalQueryPostHandler : IQueryPostHandler
{
    public Task PostHandleAsync(IQuery message, object messageResult, CancellationToken cancellationToken = default)
    {
        (message as FakeParentQuery)!.ExecutedTypes.Add(typeof(FakeGlobalQueryPostHandler));
        return Task.CompletedTask;
    }
}