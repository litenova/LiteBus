using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericCommand.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericCommand.PreHandlers;

public class FakeGenericCommandPreHandler<TPayload> : ICommandPreHandler<FakeGenericCommand<TPayload>>
{
    public Task HandleAsync(IHandleContext<FakeGenericCommand<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericCommandPreHandler<TPayload>));
        return Task.CompletedTask;
    }
}