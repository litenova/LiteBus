using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericCommandWithoutResult.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericCommandWithoutResult.PreHandlers;

public class
    FakeGenericCommandWithoutResultSyncPreHandler<TPayload> : ISyncCommandPreHandler<
        FakeGenericCommandWithoutResult<TPayload>>
{
    public void Handle(IHandleContext<FakeGenericCommandWithoutResult<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericCommandWithoutResultSyncPreHandler<TPayload>));
    }
}