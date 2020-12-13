using System.Threading;
using System.Threading.Tasks;
using Paykan.Abstractions;

namespace Paykan.WebApi.Crqs
{
    public class CreateUserCommand : ICommand<int>
    {
        public string Name { get; set; }
    }
    
    public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, int>
    {
        public Task<int> HandleAsync(CreateUserCommand input, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }
    }
}