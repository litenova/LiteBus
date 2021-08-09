using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.Commands
{
    public class CreateNumberCommandPostHandleAsyncHook : ICommandPostHandleHook<CreateNumberCommand>
    {
        public Task ExecuteAsync(CreateNumberCommand message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(CreateNumberCommandPostHandleAsyncHook)} executed!");

            return Task.CompletedTask;
        }
    }
}