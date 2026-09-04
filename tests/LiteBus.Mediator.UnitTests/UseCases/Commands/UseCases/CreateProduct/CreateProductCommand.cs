using LiteBus.Commands.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases.CreateProduct;

public sealed class CreateProductCommand : IAuditableCommand, ICommand<CreateProductCommandResult>
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public bool AnswerFromShortcut { get; set; }

    public List<Type> ExecutedTypes { get; } = new();
}