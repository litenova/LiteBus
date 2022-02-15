using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericStreamQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericStreamQuery.PreHandlers;

public class FakeGenericStreamQueryPreHandler<TPayload> : IQueryPreHandler<FakeGenericStreamQuery<TPayload>>
{
    public Task HandleAsync(IHandleContext<FakeGenericStreamQuery<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericStreamQueryPreHandler<TPayload>));
        return Task.CompletedTask;
    }
}