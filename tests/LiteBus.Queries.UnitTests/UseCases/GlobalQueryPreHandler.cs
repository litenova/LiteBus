using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases;

public sealed class GlobalQueryPreHandler : IQueryPreHandler
{
    public Task PreHandleAsync(IQuery message, CancellationToken cancellationToken = default)
    {
        if (message is IAuditableQuery auditableQuery)
        {
            auditableQuery.ExecutedTypes.Add(GetType());
        }

        return Task.CompletedTask;
    }
}