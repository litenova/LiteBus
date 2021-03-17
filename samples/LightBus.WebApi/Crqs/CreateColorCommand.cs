using System.Threading;
using System.Threading.Tasks;
using LightBus.Commands.Abstractions;

namespace LightBus.WebApi.Crqs
{
    public class CreateColorCommand : ICommand
    {
        public string ColorName { get; set; }
    }

    public class CreateColorCommandHandler : ICommandHandler<CreateColorCommand>
    {
        public Task HandleAsync(CreateColorCommand message, CancellationToken cancellationToken = default)
        {
            MemoryDatabase.AddColor(message.ColorName);

            return Task.CompletedTask;
        }
    }
}