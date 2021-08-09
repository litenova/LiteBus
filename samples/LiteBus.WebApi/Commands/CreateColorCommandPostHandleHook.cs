using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using MorseCode.ITask;

namespace LiteBus.WebApi.Commands
{
    public class CreateColorCommandPostHandleHook : ICommandPostHandleHook<CreateColorCommand>
    {
        public ITask ExecuteAsync(CreateColorCommand message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(CreateColorCommandPostHandleHook)} executed!");

            return Task.CompletedTask.AsITask();
        }
    }
}