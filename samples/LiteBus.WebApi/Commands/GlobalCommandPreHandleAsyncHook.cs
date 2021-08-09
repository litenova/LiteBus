using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using MorseCode.ITask;

namespace LiteBus.WebApi.Commands
{
    public class GlobalCommandPreHandleAsyncHook : ICommandPreHandleAsyncHook
    {
        public ITask ExecuteAsync(ICommand message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(GlobalCommandPreHandleAsyncHook)} executed!");

            return Task.CompletedTask.AsITask();
        }
    }
}