using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.WebApi.Commands;

public class GlobalCommandPreHandler : ICommandPreHandler
{
    public Task PreHandleAsync(IHandleContext<ICommandBase> context)
    {
        Debug.WriteLine($"{nameof(GlobalCommandPreHandler)} executed!");

        return Task.CompletedTask;
    }
}