using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.WebApi.Commands
{
    public class GlobalCommandPreHandleHook : ICommandPreHandleHook
    {
        public Task ExecuteAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(GlobalCommandPreHandleHook)} executed!");

            return Task.CompletedTask;
        }
    }
}