using LiteBus.Commands.Abstractions;

namespace LiteBus.MessageModule.UnitTests.Data.FakeCommand.PreHandlers;

public sealed class FakeCommandPreHandler : ICommandPreHandler<Messages.FakeCommand>
{
    public Task PreHandleAsync(Messages.FakeCommand message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeCommandPreHandler));
        return Task.CompletedTask;
    }
}