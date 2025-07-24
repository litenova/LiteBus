using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.QueryModule.UnitTests.UseCases.QueryWithTag;

[HandlerTag(Tags.Tag2)]
public sealed class QueryWithTagHandler2 : IQueryHandler<QueryWithTag, QueryWithTagResult>
{
    public Task<QueryWithTagResult> HandleAsync(QueryWithTag message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.FromResult(new QueryWithTagResult());
    }
}