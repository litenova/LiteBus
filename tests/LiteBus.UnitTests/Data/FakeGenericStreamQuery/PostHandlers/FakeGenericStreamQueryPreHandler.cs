using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeGenericStreamQuery.PostHandlers;

public class FakeGenericStreamQueryPostHandler<TPayload> : IQueryPostHandler<Messages.FakeGenericStreamQuery<TPayload>>
{
    public Task HandleAsync(IHandleContext<Messages.FakeGenericStreamQuery<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericStreamQueryPostHandler<TPayload>));
        return Task.CompletedTask;
    }
}