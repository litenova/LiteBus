using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericQuery.PreHandlers;

public class FakeGenericQueryPreHandler<TPayload> : IQueryPreHandler<FakeGenericQuery<TPayload>>
{
    public Task HandleAsync(IHandleContext<FakeGenericQuery<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericQueryPreHandler<TPayload>));
        return Task.CompletedTask;
    }
}