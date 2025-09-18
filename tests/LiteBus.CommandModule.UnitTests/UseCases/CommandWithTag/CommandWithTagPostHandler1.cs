using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.CommandModule.UnitTests.UseCases.CommandWithTag;

[HandlerPriority(1)]
[HandlerTag(Tags.Tag1)]
public sealed class CommandWithTagPostHandler1 : ICommandPostHandler<CommandWithTag>
{
    public Task PostHandleAsync(CommandWithTag message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}