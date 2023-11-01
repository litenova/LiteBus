using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.CreateProduct;

public sealed class CreateProductCommand : IAuditableCommand, ICommand<CreateProductCommandResult>
{
    public List<Type> ExecutedTypes { get; } = new();

    public Guid CorrelationId { get; } = Guid.NewGuid();
}