using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases.QueryWithTag;

[HandlerOrder(1)]
[HandlerTag(Tags.Tag1)]
public sealed class QueryWithTagPostHandler1 : IQueryPostHandler<QueryWithTag>
{
    public Task PostHandleAsync(QueryWithTag message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}