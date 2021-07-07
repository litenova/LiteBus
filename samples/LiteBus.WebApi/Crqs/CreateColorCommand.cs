using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.Crqs
{
    public class CreateColorCommand : ICommand
    {
        public string ColorName { get; set; }
    }

    public class CreateColorCommandWithResult : ICommand<bool>
    {
        public string ColorName { get; set; }
    }

    public class CreateColorCommandHandler : ICommandHandler<CreateColorCommand>
    {
        public Task HandleAsync(CreateColorCommand message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("CreateColorCommandHandler executed!");

            MemoryDatabase.AddColor(message.ColorName);

            return Task.CompletedTask;
        }
    }

    public class CreateColorCommandWithResultHandler : ICommandHandler<CreateColorCommandWithResult, bool>
    {
        public Task<bool> HandleAsync(CreateColorCommandWithResult message,
                                      CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("CreateColorCommandWithResultHandler executed!");

            MemoryDatabase.AddColor(message.ColorName);

            return Task.FromResult(true);
        }
    }
}