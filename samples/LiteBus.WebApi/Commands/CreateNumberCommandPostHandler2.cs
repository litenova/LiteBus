using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.WebApi.Commands;

public class CreateNumberCommandPostHandler2 : ICommandPostHandler<CreateNumberCommandWithResult, string>
{
    public Task PostHandleAsync(IHandleContext<CreateNumberCommandWithResult, string> context)
    {
        Debug.WriteLine($"{nameof(CreateNumberCommandPostHandler2)} executed!");

        return Task.CompletedTask;
    }
}