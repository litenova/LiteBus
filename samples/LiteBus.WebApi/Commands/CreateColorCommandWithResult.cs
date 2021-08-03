using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using MorseCode.ITask;

namespace LiteBus.WebApi.Commands
{
    public class CreateColorCommandWithResult : ICommand<bool>
    {
        public string ColorName { get; set; }
    }
    
    public class CreateColorCommandWithResultHandler : ICommandHandler<CreateColorCommandWithResult, bool>
    {
        public ITask<bool> HandleAsync(CreateColorCommandWithResult message,
                                       CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(CreateColorCommandWithResultHandler)} executed!");

            MemoryDatabase.AddColor(message.ColorName);

            return Task.FromResult(true).AsITask();
        }
    }
}