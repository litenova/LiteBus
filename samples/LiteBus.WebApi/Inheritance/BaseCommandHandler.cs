using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.Inheritance
{
    public class BaseCommandHandler : ICommandHandler<BaseCommand>
    {
        public Task HandleAsync(BaseCommand message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(BaseCommandHandler)} executed! Command Type: {message.GetType().Name}");

            return Task.CompletedTask;
        }
    }
}