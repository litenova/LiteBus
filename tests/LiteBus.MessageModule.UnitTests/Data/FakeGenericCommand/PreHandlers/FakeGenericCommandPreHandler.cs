using LiteBus.Commands.Abstractions;
using LiteBus.MessageModule.UnitTests.Data.FakeGenericCommand.Messages;

namespace LiteBus.MessageModule.UnitTests.Data.FakeGenericCommand.PreHandlers;

public sealed class FakeGenericCommandPreHandler<TPayload> : ICommandPreHandler<FakeGenericCommand<TPayload>>
{
    public Task PreHandleAsync(FakeGenericCommand<TPayload> message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericCommandPreHandler<TPayload>));
        return Task.CompletedTask;
    }
}