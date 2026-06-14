using LiteBus.Commands.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases.ProblematicCommand;

public sealed class ProblematicCommandHandler : ICommandHandler<ProblematicCommand, ProblematicCommandResult>
{
    public Task<ProblematicCommandResult> HandleAsync(ProblematicCommand message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        if (message.ThrowExceptionInType == GetType())
        {
            throw new CommandException();
        }

        return Task.FromResult(new ProblematicCommandResult
        {
            CorrelationId = message.CorrelationId
        });
    }
}