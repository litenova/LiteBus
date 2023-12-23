using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases.QueryWithTag;

[HandlerTag(Tags.Tag1)]
public sealed class QueryWithTagHandler1 : IQueryHandler<QueryWithTag, QueryWithTagResult>
{
    public Task<QueryWithTagResult> HandleAsync(QueryWithTag message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.FromResult(new QueryWithTagResult());
    }
}