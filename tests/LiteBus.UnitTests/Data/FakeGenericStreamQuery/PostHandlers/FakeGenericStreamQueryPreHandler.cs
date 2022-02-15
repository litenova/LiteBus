using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericStreamQuery.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericStreamQuery.PostHandlers;

public class FakeGenericStreamQueryPostHandler<TPayload> : IQueryPostHandler<FakeGenericStreamQuery<TPayload>>
{
    public Task HandleAsync(IHandleContext<FakeGenericStreamQuery<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericStreamQueryPostHandler<TPayload>));
        return Task.CompletedTask;
    }
}