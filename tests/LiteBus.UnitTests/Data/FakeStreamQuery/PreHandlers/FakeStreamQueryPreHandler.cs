using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.UnitTests.Data.FakeStreamQuery.PreHandlers;

public class FakeStreamQueryPreHandler : IQueryPreHandler<Messages.FakeStreamQuery>
{
    public Task HandleAsync(IHandleContext<Messages.FakeStreamQuery> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeStreamQueryPreHandler));
        return Task.CompletedTask;
    }
}