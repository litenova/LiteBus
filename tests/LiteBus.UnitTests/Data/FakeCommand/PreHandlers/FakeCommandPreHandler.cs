using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.FakeCommand.PreHandlers;

public class FakeCommandPreHandler : ICommandPreHandler<Messages.FakeCommand>
{
    public Task HandleAsync(IHandleContext<Messages.FakeCommand> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeCommandPreHandler));
        return Task.CompletedTask;
    }
}