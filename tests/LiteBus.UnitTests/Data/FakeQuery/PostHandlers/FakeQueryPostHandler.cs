using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.FakeQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeQuery.PostHandlers;

public class FakeQueryPostHandler : IQueryPostHandler<Messages.FakeQuery, FakeQueryResult>
{
    public Task HandleAsync(IHandleContext<Messages.FakeQuery, FakeQueryResult> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeQueryPostHandler));
        return Task.CompletedTask;
    }
}