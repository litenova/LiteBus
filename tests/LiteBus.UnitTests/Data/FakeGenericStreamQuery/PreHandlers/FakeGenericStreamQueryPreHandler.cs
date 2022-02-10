using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeGenericStreamQuery.PreHandlers;

public class FakeGenericStreamQueryPreHandler<TPayload> : IQueryPreHandler<Messages.FakeGenericStreamQuery<TPayload>>
{
    public Task HandleAsync(IHandleContext<Messages.FakeGenericStreamQuery<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericStreamQueryPreHandler<TPayload>));
        return Task.CompletedTask;
    }
}