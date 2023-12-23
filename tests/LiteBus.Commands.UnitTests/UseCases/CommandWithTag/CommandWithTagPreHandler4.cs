using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.CommandWithTag;

[HandlerTag(Tags.Tag1)]
[HandlerTag(Tags.Tag2)]
public sealed class CommandWithTagPreHandler4 : ICommandPreHandler<CommandWithTag>
{
    public Task PreHandleAsync(CommandWithTag message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}