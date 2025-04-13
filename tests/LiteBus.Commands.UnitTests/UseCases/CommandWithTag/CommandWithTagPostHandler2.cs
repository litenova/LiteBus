using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.CommandWithTag;

[HandlerOrder(2)]
[HandlerTag(Tags.Tag2)]
public sealed class CommandWithTagPostHandler2 : ICommandPostHandler<CommandWithTag>
{
    public Task PostHandleAsync(CommandWithTag message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}