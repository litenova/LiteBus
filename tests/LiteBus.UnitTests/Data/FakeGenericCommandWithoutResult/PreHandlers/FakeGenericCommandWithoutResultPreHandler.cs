using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericCommandWithoutResult.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericCommandWithoutResult.PreHandlers;

public class
    FakeGenericCommandWithoutResultPreHandler<TPayload> : ICommandPreHandler<FakeGenericCommandWithoutResult<TPayload>>
{
    public Task HandleAsync(IHandleContext<FakeGenericCommandWithoutResult<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericCommandWithoutResultPreHandler<TPayload>));
        return Task.CompletedTask;
    }
}