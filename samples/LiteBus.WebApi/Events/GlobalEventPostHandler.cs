using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.WebApi.Events;

public class GlobalEventPostHandler : IEventPostHandler
{
    public Task PostHandleAsync(IHandleContext<IEvent> context)
    {
        Debug.WriteLine($"{nameof(GlobalEventPostHandler)} executed!");

        return Task.CompletedTask;
    }
}