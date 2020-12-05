using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Paykan.Abstractions;

namespace Paykan.WebApi.Crqs
{
    public class ColorCreatedEvent : IEvent
    {
        public string ColorName { get; set; }
    }
    
    public class ColorCreatedEventHandler1 : IEventHandler<ColorCreatedEvent>
    {
        public Task HandleAsync(ColorCreatedEvent input, CancellationToken cancellation = default)
        {
            Debug.WriteLine("Event handled in ColorCreatedEventHandler 1");
            return Task.CompletedTask;
        }
    }
    
    public class ColorCreatedEventHandler2: IEventHandler<ColorCreatedEvent>
    {
        public Task HandleAsync(ColorCreatedEvent input, CancellationToken cancellation = default)
        {
            Debug.WriteLine("Event handled in ColorCreatedEventHandler 2");
            return Task.CompletedTask;
        }
    }
}