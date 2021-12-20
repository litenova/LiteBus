using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.Commands;

public class CreateNumberCommandWithResultHandler : ICommandHandler<CreateNumberCommandWithResult, string>
{
    public Task<string> HandleAsync(CreateNumberCommandWithResult message,
                                    CancellationToken cancellationToken = default)
    {
        Debug.WriteLine($"{nameof(CreateNumberCommandWithResultHandler)} executed!");

        MemoryDatabase.AddNumber(message.Number);

        return Task.FromResult($"The given number of $'{message.Number}' have been saved");
    }
}