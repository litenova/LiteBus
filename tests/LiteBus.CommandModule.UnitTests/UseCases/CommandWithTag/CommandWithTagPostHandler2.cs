using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.CommandModule.UnitTests.UseCases.CommandWithTag;

[HandlerPriority(2)]
[HandlerTag(Tags.Tag2)]
public sealed class CommandWithTagPostHandler2 : ICommandPostHandler<CommandWithTag>
{
    public Task PostHandleAsync(CommandWithTag message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}