using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;

namespace LiteBus.WebApi.Events;

public class NumberCreatedEventHandler2 : IEventHandler<NumberCreatedEvent>
{
    public Task HandleAsync(NumberCreatedEvent message, CancellationToken cancellationToken = default)
    {
        Debug.WriteLine($"{nameof(NumberCreatedEventHandler2)} executed! Number: {message.Number}");

        return Task.CompletedTask;
    }
}