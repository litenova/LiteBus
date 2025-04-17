using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.CommandWithTag;

public sealed class CommandWithTag : IAuditableCommand, ICommand
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public List<Type> ExecutedTypes { get; } = new();
}