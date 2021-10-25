using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.WebApi.CommandsWithError
{
    public class ECommandErrorHandler : ICommandErrorHandler<ECommand>
    {
        public Task HandleErrorAsync(IHandleContext<ECommand> context)
        {
            Debug.WriteLine($"{nameof(ECommandErrorHandler)} executed! with error '{context.Exception.Message}'");
            
            return Task.CompletedTask;
        }
    }
}