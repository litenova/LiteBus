using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeCommandWithoutResult.PreHandlers;

public class FakeCommandWithoutResultPreHandler : ICommandPreHandler<FakeCommandWithoutResult.Messages.FakeCommandWithoutResult>
{
    public Task HandleAsync(IHandleContext<FakeCommandWithoutResult.Messages.FakeCommandWithoutResult> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeCommandWithoutResultPreHandler));
        return Task.CompletedTask;
    }
}