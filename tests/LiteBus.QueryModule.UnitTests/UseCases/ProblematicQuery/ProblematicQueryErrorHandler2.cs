using LiteBus.Queries.Abstractions;

namespace LiteBus.QueryModule.UnitTests.UseCases.ProblematicQuery;

public sealed class ProblematicQueryErrorHandler2 : IQueryErrorHandler<ProblematicQuery>
{
    public Task HandleErrorAsync(ProblematicQuery message, object? messageResult, Exception exception, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }
}