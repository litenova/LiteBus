using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.Shared.Queries;

namespace LiteBus.UnitTests.Data.Shared.QueryGlobalPreHandlers;

public class FakeGlobalQueryPreHandler : IQueryPreHandler
{
    public Task PreHandleAsync(IHandleContext<IQueryBase> context)
    {
        (context.Message as FakeParentQuery)!.ExecutedTypes.Add(typeof(FakeGlobalQueryPreHandler));
        return Task.CompletedTask;
    }
}