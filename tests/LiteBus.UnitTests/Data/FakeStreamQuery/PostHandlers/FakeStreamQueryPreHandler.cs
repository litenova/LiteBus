using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.UnitTests.Data.FakeStreamQuery.PostHandlers;

public class FakeStreamQueryPostHandler : IQueryPostHandler<Messages.FakeStreamQuery>
{
    public Task HandleAsync(IHandleContext<Messages.FakeStreamQuery> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeStreamQueryPostHandler));
        return Task.CompletedTask;
    }
}