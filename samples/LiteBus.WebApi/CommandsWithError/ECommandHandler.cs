using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.CommandsWithError
{
    public class ECommandHandler : ICommandHandler<ECommand>
    {
        public Task HandleAsync(ECommand message, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }
    }
}