using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeGenericCommand.PreHandlers;

public class FakeGenericCommandPreHandler<TPayload> : ICommandPreHandler<Messages.FakeGenericCommand<TPayload>>
{
    public Task PreHandleAsync(IHandleContext<Messages.FakeGenericCommand<TPayload>> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericCommandPreHandler<TPayload>));
        return Task.CompletedTask;
    }
}