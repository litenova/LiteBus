using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeCommandWithoutResult.PostHandlers;

public class FakeCommandWithoutResultPostHandler : ICommandPostHandler<FakeCommandWithoutResult.Messages.FakeCommandWithoutResult>
{
    public Task HandleAsync(IHandleContext<FakeCommandWithoutResult.Messages.FakeCommandWithoutResult> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeCommandWithoutResultPostHandler));
        return Task.CompletedTask;
    }
}