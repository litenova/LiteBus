using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeCommandWithoutResult.PostHandlers;

public class FakeCommandWithoutResultPostHandler : ICommandPostHandler<Messages.FakeCommandWithoutResult>
{
    public Task HandleAsync(IHandleContext<Messages.FakeCommandWithoutResult> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeCommandWithoutResultPostHandler));
        return Task.CompletedTask;
    }
}