using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.ProblematicCommand;

public sealed class ProblematicCommand : IAuditableCommand, ICommand<ProblematicCommandResult>
{
    public List<Type> ExecutedTypes { get; } = new();

    public Guid CorrelationId { get; } = Guid.NewGuid();

    public required Type ThrowExceptionInType { get; init; }
}