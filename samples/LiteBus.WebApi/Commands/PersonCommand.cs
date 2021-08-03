using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using MorseCode.ITask;

namespace LiteBus.WebApi.Commands
{
    public class CreatePersonCommand : ICommand
    {
        public string FirstName { get; set; }
    }

    public class CreateDoctorCommand : CreatePersonCommand
    {
        public string Speciality { get; set; }
    }

    public class CreateSoldierCommand : CreatePersonCommand
    {
        public string Rank { get; set; }
    }
    
    public class CreatePersonCommandHandler : ICommandHandler<CreatePersonCommand>
    {
        public ITask HandleAsync(CreatePersonCommand message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(CreatePersonCommandHandler)} executed!");

            return Task.CompletedTask.AsITask();
        }
    }
    
    public class CreateSoldierCommandHandler : ICommandHandler<CreateSoldierCommand>
    {
        public ITask HandleAsync(CreateSoldierCommand message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(CreateSoldierCommandHandler)} executed!");

            return Task.CompletedTask.AsITask();
        }
    }
}