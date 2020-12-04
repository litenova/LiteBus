using System.Threading;
using System.Threading.Tasks;
using Paykan.Abstractions;

namespace Paykan.WebApi.Crqs
{
    public class CreateColorCommand : ICommand
    {
        public string Color { get; set; }
    }

    public class CreateColorCommandHandler : ICommandHandler<CreateColorCommand>
    {
        public Task HandleAsync(CreateColorCommand input, CancellationToken cancellation = default)
        {
            MemoryDatabase.AddColor(input.Color);
            return Task.CompletedTask;
        }
    }

}