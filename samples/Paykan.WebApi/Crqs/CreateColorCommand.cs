using System.Threading;
using System.Threading.Tasks;
using Paykan.Abstractions;

namespace Paykan.WebApi.Crqs
{
    public class CreateColorCommand : ICommand
    {
        public string ColorName { get; set; }
    }

    public class CreateColorCommandHandler : ICommandHandler<CreateColorCommand>
    {
        public Task HandleAsync(CreateColorCommand input, CancellationToken cancellation = default)
        {
            MemoryDatabase.AddColor(input.ColorName);
            return Task.CompletedTask;
        }
    }

    public class CreateColorCommandHandler1 : ICommandHandler<CreateColorCommand>
    {
        public Task HandleAsync(CreateColorCommand input, CancellationToken cancellation = default)
        {
            MemoryDatabase.AddColor(input.ColorName);
            return Task.CompletedTask;
        }
    }
}