using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;

namespace LiteBus.WebApi.Events
{
    public class ColorCreatedEvent : IEvent
    {
        public string ColorName { get; set; }
    }

    public class ColorCreatedEventHandler1 : IEventHandler<ColorCreatedEvent>
    {
        public Task HandleAsync(ColorCreatedEvent message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(ColorCreatedEventHandler1)} executed!");

            return Task.CompletedTask;
        }
    }

    public class ColorCreatedEventHandler2 : IEventHandler<ColorCreatedEvent>
    {
        public Task HandleAsync(ColorCreatedEvent message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(ColorCreatedEventHandler2)} executed!");

            return Task.CompletedTask;
        }
    }
}