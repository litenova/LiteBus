using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.WebApi.Commands
{
    public class GlobalCommandPostHandleHook : ICommandPostHandleHook
    {
        public Task ExecuteAsync(ICommand message, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }
    }
}