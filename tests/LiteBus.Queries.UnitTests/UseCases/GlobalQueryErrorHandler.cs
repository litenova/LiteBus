using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases;

public class GlobalQueryErrorHandler : IQueryErrorHandler
{
    public Task HandleErrorAsync(IQuery message, object messageResult, CancellationToken cancellationToken = default)
    {
        if (message is IAuditableQuery auditableQuery)
        {
            auditableQuery.ExecutedTypes.Add(GetType());
        }

        return Task.CompletedTask;
    }
}