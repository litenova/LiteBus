using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.ProblematicCommand;

[HandlerOrder(1)]
public sealed class ProblematicCommandPostHandler : ICommandPostHandler<ProblematicCommand, ProblematicCommandResult>
{
    public Task PostHandleAsync(ProblematicCommand message, ProblematicCommandResult messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        
        if (message.ThrowExceptionInType == GetType())
        {
            throw new CommandException();
        }
        
        return Task.CompletedTask;
    }
}