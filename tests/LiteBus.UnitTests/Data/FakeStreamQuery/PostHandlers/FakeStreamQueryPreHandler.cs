using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeStreamQuery.PostHandlers;

public class FakeStreamQueryPostHandler : IQueryPostHandler<FakeStreamQuery.Messages.FakeStreamQuery>
{
    public Task HandleAsync(IHandleContext<FakeStreamQuery.Messages.FakeStreamQuery> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeStreamQueryPostHandler));
        return Task.CompletedTask;
    }
}