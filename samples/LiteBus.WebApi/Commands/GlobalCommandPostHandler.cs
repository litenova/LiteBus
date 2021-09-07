using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.WebApi.Commands
{
    public class GlobalCommandPostHandler : ICommandPostHandler
    {
        public Task PostHandleAsync(IHandleContext<ICommandBase> context)
        {
            Debug.WriteLine($"{nameof(GlobalCommandPostHandler)} executed!");

            return Task.CompletedTask;
        }
    }
}