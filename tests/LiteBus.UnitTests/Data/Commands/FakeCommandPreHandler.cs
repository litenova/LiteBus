using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.Commands;

public class FakeCommandPreHandler : ICommandPreHandler<FakeCommand>
{
    public Task PreHandleAsync(IHandleContext<FakeCommand> context)
    {
        context.Message.ExecutedTypes.Add(typeof(FakeCommandPreHandler));
        return Task.CompletedTask;
    }
}