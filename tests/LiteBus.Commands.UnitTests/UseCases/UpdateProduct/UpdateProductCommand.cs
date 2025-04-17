using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.UpdateProduct;

public sealed class UpdateProductCommand : IAuditableCommand, ICommand
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public List<Type> ExecutedTypes { get; } = new();
}