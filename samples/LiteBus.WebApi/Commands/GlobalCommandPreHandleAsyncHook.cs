using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.Commands
{
    public class GlobalCommandPreHandleAsyncHook : ICommandPreHandleAsyncHook
    {
        public Task ExecuteAsync(ICommandBase message, CancellationToken cancellationToken)
        {
            Debug.WriteLine($"{nameof(GlobalCommandPreHandleAsyncHook)} executed!");

            return Task.CompletedTask;
        }
    }
}