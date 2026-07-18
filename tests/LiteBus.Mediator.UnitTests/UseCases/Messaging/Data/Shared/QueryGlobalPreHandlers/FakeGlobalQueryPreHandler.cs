using LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.Shared.Queries;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.Shared.QueryGlobalPreHandlers;

public sealed class FakeGlobalQueryPreHandler : IQueryPreHandler
{
    public Task PreHandleAsync(IQuery message, CancellationToken cancellationToken = default)
    {
        (message as FakeParentQuery)!.ExecutedTypes.Add(typeof(FakeGlobalQueryPreHandler));
        return Task.CompletedTask;
    }
}