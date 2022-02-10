using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.UnitTests.Data.FakeQuery.PreHandlers;

public class FakeSyncQueryPreHandler : ISyncQueryPreHandler<FakeQuery.Messages.FakeQuery>
{
    public void Handle(IHandleContext<Messages.FakeQuery> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeSyncQueryPreHandler));
    }
}