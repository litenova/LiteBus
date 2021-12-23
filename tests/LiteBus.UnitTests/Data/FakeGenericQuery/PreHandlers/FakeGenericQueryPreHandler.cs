using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeGenericQuery.PreHandlers;

public class FakeGenericQueryPreHandler<TPayload> : IQueryPreHandler<Messages.FakeGenericQuery<TPayload>>
{
    public Task PreHandleAsync(IHandleContext<Messages.FakeGenericQuery<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericQueryPreHandler<TPayload>));
        return Task.CompletedTask;
    }
}