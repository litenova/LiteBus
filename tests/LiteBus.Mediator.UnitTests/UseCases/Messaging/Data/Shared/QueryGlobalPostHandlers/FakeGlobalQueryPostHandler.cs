using LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.Shared.Queries;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.Shared.QueryGlobalPostHandlers;

public sealed class FakeGlobalQueryPostHandler : IQueryPostHandler
{
    public Task PostHandleAsync(IQuery message, object? messageResult, CancellationToken cancellationToken = default)
    {
        (message as FakeParentQuery)!.ExecutedTypes.Add(typeof(FakeGlobalQueryPostHandler));
        return Task.CompletedTask;
    }
}