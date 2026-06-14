using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.GetProduct;

public sealed class GetProductQueryHandlerPreHandler : IQueryPreHandler<GetProductQuery>
{
    public Task PreHandleAsync(GetProductQuery message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}