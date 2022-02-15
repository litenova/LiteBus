using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericCommand.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericCommand.PreHandlers;

public class FakeGenericCommandSyncPreHandler<TPayload> : ISyncCommandPreHandler<Messages.FakeGenericCommand<TPayload>>
{
    public void Handle(IHandleContext<FakeGenericCommand<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericCommandSyncPreHandler<TPayload>));
    }
}