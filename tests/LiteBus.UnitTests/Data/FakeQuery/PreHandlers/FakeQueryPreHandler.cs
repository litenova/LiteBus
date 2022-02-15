using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.UnitTests.Data.FakeQuery.PreHandlers;

public class FakeQueryPreHandler : IQueryPreHandler<Messages.FakeQuery>
{
    public Task HandleAsync(IHandleContext<Messages.FakeQuery> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeQueryPreHandler));
        return Task.CompletedTask;
    }
}