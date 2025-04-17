using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases.ProblematicQuery;

public sealed class ProblematicQueryPreHandler : IQueryPreHandler<ProblematicQuery>
{
    public Task PreHandleAsync(ProblematicQuery message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        if (message.ThrowExceptionInType == GetType())
        {
            throw new Exception();
        }

        return Task.CompletedTask;
    }
}