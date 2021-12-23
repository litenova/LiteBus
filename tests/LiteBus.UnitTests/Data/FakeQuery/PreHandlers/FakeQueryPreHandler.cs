using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeQuery.PreHandlers;

public class FakeQueryPreHandler : IQueryPreHandler<FakeQuery.Messages.FakeQuery>
{
    public Task PreHandleAsync(IHandleContext<FakeQuery.Messages.FakeQuery> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeQueryPreHandler));
        return Task.CompletedTask;
    }
}