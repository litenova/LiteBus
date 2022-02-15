using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericCommandWithoutResult.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericCommandWithoutResult.PostHandlers;

public class FakeGenericCommandWithoutResultPostHandler<TPayload> : ICommandPostHandler<FakeGenericCommandWithoutResult<TPayload>>
{
    public Task HandleAsync(IHandleContext<FakeGenericCommandWithoutResult<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericCommandWithoutResultPostHandler<TPayload>));
        return Task.CompletedTask;
    }
}