using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.Crqs
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
        public Task Handle(CreatePersonCommand message)
        {
            Debug.WriteLine("CreatePersonCommandHandler executed!");

            return Task.CompletedTask;
        }
    }
    
    public class CreateSoldierCommandHandler : ICommandHandler<CreateSoldierCommand>
    {
        public Task Handle(CreateSoldierCommand message)
        {
            Debug.WriteLine("CreateSoldierCommandHandler executed!");

            return Task.CompletedTask;
        }
    }
}