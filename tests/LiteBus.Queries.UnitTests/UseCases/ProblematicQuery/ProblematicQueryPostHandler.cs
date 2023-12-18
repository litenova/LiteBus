using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases.ProblematicQuery;

[HandlerOrder(1)]
public sealed class ProblematicQueryPostHandler : IQueryPostHandler<ProblematicQuery, ProblematicQueryResult>
{
    public Task PostHandleAsync(ProblematicQuery message, ProblematicQueryResult messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        
        if (message.ThrowExceptionInType == GetType())
        {
            throw new Exception();
        }
        
        return Task.CompletedTask;
    }
}