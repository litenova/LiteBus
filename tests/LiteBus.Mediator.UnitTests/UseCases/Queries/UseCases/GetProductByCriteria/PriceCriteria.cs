namespace LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.GetProductByCriteria;

public class PriceCriteria
{
    public required double Min { get; init; }

    public required double Max { get; init; }
}