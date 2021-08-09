using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.Commands
{
    public class GlobalCommandPostHandleAsyncHook : ICommandPostHandleHook
    {
        public Task ExecuteAsync(ICommandBase message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(GlobalCommandPostHandleAsyncHook)} executed!");

            return Task.CompletedTask;
        }
    }
}