using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries.UnitTests.UseCases.GetProduct;

public sealed class GetProductQueryHandler : IQueryHandler<GetProductQuery, GetProductQueryResult>
{
    public Task<GetProductQueryResult> HandleAsync(GetProductQuery message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.FromResult(new GetProductQueryResult
        {
            CorrelationId = message.CorrelationId
        });
    }
}