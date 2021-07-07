using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.Commands
{
    public class CreateColorCommand : ICommand
    {
        public string ColorName { get; set; }
    }

    public class CreateColorCommandHandler : ICommandHandler<CreateColorCommand>
    {
        public Task HandleAsync(CreateColorCommand message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(CreateColorCommandHandler)} executed!");

            MemoryDatabase.AddColor(message.ColorName);

            return Task.CompletedTask;
        }
    }
}