using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BasicBus.Abstractions;

namespace BasicBus.WebApi.Commands
{
    public class CreateUserCommand : ICommand
    {
        public string Name { get; set; }
    }

    public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
    {
        public Task HandleAsync(CreateUserCommand command, CancellationToken cancellation = default)
        {
            Debug.Write("Handling CreateUserCommand Command");
            return Task.CompletedTask;
        }
    }

    public class CreateUserCommandInterceptor : ICommandInterceptor<CreateUserCommand>
    {
        public Task OnPreHandleAsync(CreateUserCommand command)
        {
            Debug.Write("Pre Handle in CreateUserCommandInterceptor");
            return Task.CompletedTask;
        }

        public Task OnPostHandleAsync(CreateUserCommand command)
        {
            Debug.Write("Post Handle in CreateUserCommandInterceptor");
            return Task.CompletedTask;
        }
    }

    public class GlobalCommandInterceptor : ICommandInterceptor
    {
        public Task OnPreHandleAsync(ICommand command)
        {
            Debug.Write("Pre Handle in GlobalCommandInterceptor");
            return Task.CompletedTask;
        }

        public Task OnPostHandleAsync(ICommand command)
        {
            Debug.Write("Post Handle in GlobalCommandInterceptor");
            return Task.CompletedTask;
        }
    }
}