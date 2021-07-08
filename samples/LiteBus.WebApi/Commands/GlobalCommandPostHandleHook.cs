using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.WebApi.Commands
{
    public class GlobalCommandPostHandleHook : ICommandPostHandleHook
    {
        public Task ExecuteAsync(IMessage message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(GlobalCommandPostHandleHook)} executed!");
            
            return Task.CompletedTask;
        }
    }
}