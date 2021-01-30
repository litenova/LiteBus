using System.Threading;
using System.Threading.Tasks;
using Paykan.Commands.Abstraction;

namespace Paykan.WebApi.Crqs
{
    public class CreateUserCommand : ICommand<int>
    {
        public string Name { get; set; }
    }
    
    public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, int>
    {
        public Task<int> HandleAsync(CreateUserCommand message, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }
    }
}