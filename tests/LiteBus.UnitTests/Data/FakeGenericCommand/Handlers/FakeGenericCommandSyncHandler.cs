using LiteBus.Commands.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericCommand.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericCommand.Handlers;

public class FakeGenericCommandSyncHandler<TPayload> : ISyncCommandHandler<FakeGenericCommand<TPayload>,
    FakeGenericCommandResult>
{
    public FakeGenericCommandResult Handle(FakeGenericCommand<TPayload> message)
    {
        message.ExecutedTypes.Add(typeof(FakeGenericCommandSyncHandler<TPayload>));
        return new FakeGenericCommandResult(message.CorrelationId);
    }
}