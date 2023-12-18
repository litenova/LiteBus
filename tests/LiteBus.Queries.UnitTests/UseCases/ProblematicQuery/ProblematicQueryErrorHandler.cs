using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases.ProblematicQuery;

public sealed class ProblematicQueryErrorHandler : IQueryErrorHandler<ProblematicQuery>
{
    public Task HandleErrorAsync(ProblematicQuery message, object messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }
}