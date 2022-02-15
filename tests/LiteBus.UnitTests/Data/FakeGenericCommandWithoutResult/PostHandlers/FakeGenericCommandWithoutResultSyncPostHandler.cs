using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericCommandWithoutResult.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericCommandWithoutResult.PostHandlers;

public class FakeGenericCommandWithoutResultSyncPostHandler<TPayload> : ISyncCommandPostHandler<FakeGenericCommandWithoutResult<TPayload>>
{
    public void Handle(IHandleContext<FakeGenericCommandWithoutResult<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericCommandWithoutResultSyncPostHandler<TPayload>));
    }
}