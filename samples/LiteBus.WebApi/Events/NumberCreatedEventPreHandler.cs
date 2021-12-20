using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.WebApi.Events;

public class NumberCreatedEventPreHandler : IEventPreHandler<NumberCreatedEvent>
{
    public Task PreHandleAsync(IHandleContext<NumberCreatedEvent> context)
    {
        Debug.WriteLine($"{nameof(NumberCreatedEventPreHandler)} executed!");

        return Task.CompletedTask;
    }
}