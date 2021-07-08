using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.Commands
{
    public class CreateColorCommandPostHandleHook : ICommandPostHandleHook<CreateColorCommand>
    {
        public Task ExecuteAsync(CreateColorCommand message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(CreateColorCommandPostHandleHook)} executed!");

            return Task.CompletedTask;
        }
    }
}