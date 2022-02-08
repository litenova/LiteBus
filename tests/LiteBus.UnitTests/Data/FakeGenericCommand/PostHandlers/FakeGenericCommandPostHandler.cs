using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.UnitTests.Data.FakeGenericCommand.Messages;

namespace LiteBus.UnitTests.Data.FakeGenericCommand.PostHandlers;

public class FakeGenericCommandPostHandler<TPayload> : ICommandPostHandler<FakeGenericCommand<TPayload>, FakeGenericCommandResult>
{
    public Task HandleAsync(IHandleContext<FakeGenericCommand<TPayload>, FakeGenericCommandResult> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeGenericCommandPostHandler<TPayload>));
        return Task.CompletedTask;
    }
}