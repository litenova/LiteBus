using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.WebApi.Events;

public class GlobalEventPreHandler : IEventPreHandler
{
    public Task PreHandleAsync(IHandleContext<IEvent> context)
    {
        Debug.WriteLine($"{nameof(GlobalEventPreHandler)} executed!");

        return Task.CompletedTask;
    }
}