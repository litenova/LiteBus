using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases.ProblematicQuery;

public sealed class ProblematicQueryHandler : IQueryHandler<ProblematicQuery, ProblematicQueryResult>
{
    public Task<ProblematicQueryResult> HandleAsync(ProblematicQuery message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        if (message.ThrowExceptionInType == GetType())
        {
            throw new Exception();
        }

        return Task.FromResult(new ProblematicQueryResult
        {
            CorrelationId = message.CorrelationId
        });
    }
}