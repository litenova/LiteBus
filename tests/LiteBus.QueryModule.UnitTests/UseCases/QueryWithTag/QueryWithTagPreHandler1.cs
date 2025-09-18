using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.QueryModule.UnitTests.UseCases.QueryWithTag;

[HandlerTag(Tags.Tag1)]
public sealed class QueryWithTagPreHandler1 : IQueryPreHandler<QueryWithTag>
{
    public Task PreHandleAsync(QueryWithTag message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}