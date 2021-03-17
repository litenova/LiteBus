using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LightBus.Events.Abstractions;

namespace LightBus.WebApi.Crqs
{
    public class ColorCreatedEvent : IEvent
    {
        public string ColorName { get; set; }
    }

    public class ColorCreatedEventHandler1 : IEventHandler<ColorCreatedEvent>
    {
        public Task HandleAsync(ColorCreatedEvent message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("Event handled in ColorCreatedEventHandler 1");

            return Task.CompletedTask;
        }
    }

    public class ColorCreatedEventHandler2 : IEventHandler<ColorCreatedEvent>
    {
        public Task HandleAsync(ColorCreatedEvent message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("Event handled in ColorCreatedEventHandler 2");

            return Task.CompletedTask;
        }
    }
}