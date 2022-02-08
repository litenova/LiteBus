using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.FakeQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeQuery.PostHandlers;

public class FakeQueryPostHandler : IQueryPostHandler<FakeQuery.Messages.FakeQuery, FakeQueryResult>
{
    public Task HandleAsync(IHandleContext<FakeQuery.Messages.FakeQuery, FakeQueryResult> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeQueryPostHandler));
        return Task.CompletedTask;
    }
}