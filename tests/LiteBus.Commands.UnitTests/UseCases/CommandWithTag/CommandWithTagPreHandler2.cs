using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.CommandWithTag;

[HandlerTag(Tags.Tag2)]
public sealed class CommandWithTagPreHandler2 : ICommandPreHandler<CommandWithTag>
{
    public Task PreHandleAsync(CommandWithTag message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}