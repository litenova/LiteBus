using LiteBus.Commands.Abstractions;

namespace LiteBus.CommandModule.UnitTests.UseCases.CreateProduct;

public sealed class CreateProductCommand : IAuditableCommand, ICommand<CreateProductCommandResult>
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public bool AbortInPreHandler { get; set; }

    public List<Type> ExecutedTypes { get; } = new();
}