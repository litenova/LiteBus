using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.WebApi.Events;

public class NumberCreatedEventPostHandler : IEventPostHandler<NumberCreatedEvent>
{
    public Task PostHandleAsync(IHandleContext<NumberCreatedEvent> context)
    {
        Debug.WriteLine($"{nameof(NumberCreatedEventPostHandler)} executed!");

        return Task.CompletedTask;
    }
}