using LiteBus.Queries.Abstractions;

namespace LiteBus.QueryModule.UnitTests.UseCases;

public class GlobalQueryErrorHandler : IQueryErrorHandler
{
    public Task HandleErrorAsync(IQuery message, object? messageResult, Exception exception, CancellationToken cancellationToken = default)
    {
        if (message is IAuditableQuery auditableQuery)
        {
            auditableQuery.ExecutedTypes.Add(GetType());
        }

        return Task.CompletedTask;
    }
}