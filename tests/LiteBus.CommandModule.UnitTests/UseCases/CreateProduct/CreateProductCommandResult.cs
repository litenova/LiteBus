namespace LiteBus.CommandModule.UnitTests.UseCases.CreateProduct;

public sealed class CreateProductCommandResult
{
    public required Guid CorrelationId { get; init; }
}