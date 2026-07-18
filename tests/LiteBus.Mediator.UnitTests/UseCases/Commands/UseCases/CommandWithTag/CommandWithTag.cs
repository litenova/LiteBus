using LiteBus.Commands.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases.CommandWithTag;

public sealed class CommandWithTag : IAuditableCommand, ICommand
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public List<Type> ExecutedTypes { get; } = new();
}