using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeStreamQuery.PreHandlers;

public class FakeStreamQueryPreHandler : IQueryPreHandler<FakeStreamQuery.Messages.FakeStreamQuery>
{
    public Task HandleAsync(IHandleContext<FakeStreamQuery.Messages.FakeStreamQuery> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeStreamQueryPreHandler));
        return Task.CompletedTask;
    }
}