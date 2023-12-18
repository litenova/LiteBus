using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.ProblematicCommand;

public sealed class ProblematicCommandErrorHandler : ICommandErrorHandler<ProblematicCommand>
{
    public Task HandleErrorAsync(ProblematicCommand message, object messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }
}