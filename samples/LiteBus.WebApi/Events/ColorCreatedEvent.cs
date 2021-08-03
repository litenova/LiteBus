using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using MorseCode.ITask;

namespace LiteBus.WebApi.Events
{
    public class ColorCreatedEvent : IEvent
    {
        public string ColorName { get; set; }
    }

    public class ColorCreatedEventHandler1 : IEventHandler<ColorCreatedEvent>
    {
        public ITask HandleAsync(ColorCreatedEvent message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(ColorCreatedEventHandler1)} executed!");

            return Task.CompletedTask.AsITask();
        }
    }

    public class ColorCreatedEventHandler2 : IEventHandler<ColorCreatedEvent>
    {
        public ITask HandleAsync(ColorCreatedEvent message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(ColorCreatedEventHandler2)} executed!");

            return Task.CompletedTask.AsITask();
        }
    }
}