using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.QueryModule.UnitTests.UseCases.QueryWithTag;

[HandlerTags(Tags.Tag1, Tags.Tag2)]
public sealed class QueryWithTagPreHandler4 : IQueryPreHandler<QueryWithTag>
{
    public Task PreHandleAsync(QueryWithTag message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}