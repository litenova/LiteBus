using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.QueryModule.UnitTests.UseCases.QueryWithTag;

[HandlerTag(Tags.Tag2)]
public sealed class QueryWithTagPreHandler2 : IQueryPreHandler<QueryWithTag>
{
    public Task PreHandleAsync(QueryWithTag message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}