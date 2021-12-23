using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericQuery.PostHandlers;

public class FakeGenericQueryPostHandler<TPayload> : IQueryPostHandler<FakeGenericQuery<TPayload>, FakeGenericQueryResult>
{
    public Task PostHandleAsync(IHandleContext<FakeGenericQuery<TPayload>, FakeGenericQueryResult> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericQueryPostHandler<TPayload>));
        return Task.CompletedTask;
    }
}