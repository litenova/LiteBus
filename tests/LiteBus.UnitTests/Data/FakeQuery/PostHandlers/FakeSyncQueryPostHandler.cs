using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.FakeQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeQuery.PostHandlers;

public class FakeSyncQueryPostHandler : ISyncQueryPostHandler<Messages.FakeQuery, FakeQueryResult>
{
    public void Handle(IHandleContext<Messages.FakeQuery, FakeQueryResult> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeSyncQueryPostHandler));
    }
}