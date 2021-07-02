using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.Crqs
{
    public class CreateColorCommandPostHandleHook : ICommandPostHandleHook<CreateColorCommand>
    {
        public Task ExecuteAsync(CreateColorCommand message, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }
    }
}