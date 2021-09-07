using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.Inheritance
{
    public class ParentCommandHandler : ICommandHandler<ParentCommand>
    {
        public Task HandleAsync(ParentCommand message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(ParentCommandHandler)} executed! Command Type: {message.GetType().Name}");

            return Task.CompletedTask;
        }
    }
}