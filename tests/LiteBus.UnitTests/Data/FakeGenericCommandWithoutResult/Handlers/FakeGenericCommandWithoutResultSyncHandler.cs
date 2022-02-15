using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericCommandWithoutResult.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericCommandWithoutResult.Handlers;

public class FakeGenericCommandWithoutResultSyncHandler<TPayload> : ISyncCommandHandler<
    FakeGenericCommandWithoutResult<TPayload>>
{
    public void Handle(FakeGenericCommandWithoutResult<TPayload> message)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericCommandWithoutResultSyncHandler<TPayload>));
    }
}