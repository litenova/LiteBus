using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases;

public sealed class GlobalQueryPostHandler : IQueryPostHandler
{
    public Task PostHandleAsync(IQuery message, object messageResult, CancellationToken cancellationToken = default)
    {
        if (message is IAuditableQuery auditableQuery)
        {
            auditableQuery.ExecutedTypes.Add(GetType());
        }

        if (message is ProblematicQuery.ProblematicQuery problematicQuery && problematicQuery.ThrowExceptionInType == GetType())
        {
            throw new Exception();
        }
        
        return Task.CompletedTask;
    }
}