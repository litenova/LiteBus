using LiteBus.Commands.Abstractions;

namespace LiteBus.CommandModule.UnitTests.UseCases.ProblematicCommand;

public sealed class ProblematicCommandErrorHandler2 : ICommandErrorHandler<ProblematicCommand>
{
    public Task HandleErrorAsync(ProblematicCommand message, object? messageResult, Exception exception, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }
}