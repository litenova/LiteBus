using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericCommand.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericCommand.PostHandlers;

public class FakeGenericCommandSyncPostHandler<TPayload> : ISyncCommandPostHandler<FakeGenericCommand<TPayload>, FakeGenericCommandResult>
{
    public void Handle(IHandleContext<FakeGenericCommand<TPayload>, FakeGenericCommandResult> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericCommandSyncPostHandler<TPayload>));
    }
}