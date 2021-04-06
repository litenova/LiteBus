using System.Diagnostics;
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
        public Task Handle(CreateColorCommand message)
        {
            Debug.WriteLine("CreateColorCommandHandler executed!");

            MemoryDatabase.AddColor(message.ColorName);

            return Task.FromResult(true);
        }
    }

    public class CreateColorCommandWithResultHandler : ICommandHandler<CreateColorCommandWithResult, bool>
    {
        public Task<bool> Handle(CreateColorCommandWithResult message)
        {
            Debug.WriteLine("CreateColorCommandWithResultHandler executed!");

            MemoryDatabase.AddColor(message.ColorName);

            return Task.FromResult(true);
        }
    }
}