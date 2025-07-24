using LiteBus.Commands.Abstractions;

namespace LiteBus.CommandModule.UnitTests.UseCases.ProblematicCommand;

public sealed class ProblematicCommandPreHandler : ICommandPreHandler<ProblematicCommand>
{
    public Task PreHandleAsync(ProblematicCommand message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        if (message.ThrowExceptionInType == GetType())
        {
            throw new CommandException();
        }

        return Task.CompletedTask;
    }
}