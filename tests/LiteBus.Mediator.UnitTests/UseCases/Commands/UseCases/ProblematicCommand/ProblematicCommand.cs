using LiteBus.Commands.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases.ProblematicCommand;

public sealed class ProblematicCommand : IAuditableCommand, ICommand<ProblematicCommandResult>
{
    public Guid CorrelationId { get; } = Guid.NewGuid();

    public required Type ThrowExceptionInType { get; init; }

    public List<Type> ExecutedTypes { get; } = new();
}