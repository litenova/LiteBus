using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.CommandWithTag;

[HandlerTag(Tags.Tag1)]
public sealed class CommandWithTagPreHandler1 : ICommandPreHandler<CommandWithTag>
{
    public Task PreHandleAsync(CommandWithTag message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}