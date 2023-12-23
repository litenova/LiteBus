using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases.QueryWithTag;

[HandlerOrder(2)]
[HandlerTag(Tags.Tag2)]
public sealed class QueryWithTagPostHandler2 : IQueryPostHandler<QueryWithTag>
{
    public Task PostHandleAsync(QueryWithTag message, object messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}