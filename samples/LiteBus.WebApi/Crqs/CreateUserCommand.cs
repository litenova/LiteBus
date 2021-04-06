using System;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.Crqs
{
    public class CreateUserCommand : ICommand<int>
    {
        public string Name { get; set; }
    }

    public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, int>
    {
        public Task<int> Handle(CreateUserCommand message)
        {
            throw new NotImplementedException();
        }
    }
}