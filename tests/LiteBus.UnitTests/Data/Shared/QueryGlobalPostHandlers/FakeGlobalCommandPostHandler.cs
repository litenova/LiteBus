using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.Shared.Queries;

namespace LiteBus.UnitTests.Data.Shared.QueryGlobalPostHandlers;

public class FakeGlobalQueryPostHandler : IQueryPostHandler
{
    public Task PostHandleAsync(IHandleContext<IQueryBase> context)
    {
        (context.Message as FakeParentQuery)!.ExecutedTypes.Add(typeof(FakeGlobalQueryPostHandler));
        return Task.CompletedTask;
    }
}