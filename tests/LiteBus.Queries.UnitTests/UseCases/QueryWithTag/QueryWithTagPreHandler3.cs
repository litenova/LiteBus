using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases.QueryWithTag;

public sealed class QueryWithTagPreHandler3 : IQueryPreHandler<QueryWithTag>
{
    public Task PreHandleAsync(QueryWithTag message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}