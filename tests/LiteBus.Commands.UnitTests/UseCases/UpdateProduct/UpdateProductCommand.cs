using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.UpdateProduct;

public sealed class UpdateProductCommand : IAuditableCommand, ICommand
{
    public List<Type> ExecutedTypes { get; } = new();

    public Guid CorrelationId { get; } = Guid.NewGuid();
}