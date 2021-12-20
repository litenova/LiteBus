using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.Commands;

public class CreateNumberCommandHandler : ICommandHandler<CreateNumberCommand>
{
    public Task HandleAsync(CreateNumberCommand message, CancellationToken cancellationToken = default)
    {
        Debug.WriteLine($"{nameof(CreateNumberCommandHandler)} executed!");

        MemoryDatabase.AddNumber(message.Number);

        return Task.CompletedTask;
    }
}