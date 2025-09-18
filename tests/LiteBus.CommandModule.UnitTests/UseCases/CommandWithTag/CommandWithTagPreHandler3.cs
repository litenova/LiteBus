using LiteBus.Commands.Abstractions;

namespace LiteBus.CommandModule.UnitTests.UseCases.CommandWithTag;

public sealed class CommandWithTagPreHandler3 : ICommandPreHandler<CommandWithTag>
{
    public Task PreHandleAsync(CommandWithTag message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}