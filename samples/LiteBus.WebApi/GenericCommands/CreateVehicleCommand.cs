using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.GenericCommands
{
    public class CreateVehicleCommand<TVehicle> : ICommand
    {
        
    }
    
    public class CreateVehicleCommandHandler<TVehicle> : ICommandHandler<CreateVehicleCommand<TVehicle>>
    {
        public Task HandleAsync(CreateVehicleCommand<TVehicle> message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(CreateVehicleCommandHandler<TVehicle>)} executed!");

            return Task.CompletedTask;
        }
    }
}