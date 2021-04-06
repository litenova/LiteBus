using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;

namespace LiteBus.WebApi.Crqs
{
    public class ColorCreatedEvent : IEvent
    {
        public string ColorName { get; set; }
    }

    public class ColorCreatedEventHandler1 : IEventHandler<ColorCreatedEvent>
    {
        public Task Handle(ColorCreatedEvent message)
        {
            Debug.WriteLine("Event handled in ColorCreatedEventHandler 1");

            return Task.CompletedTask;
        }
    }

    public class ColorCreatedEventHandler2 : IEventHandler<ColorCreatedEvent>
    {
        public Task Handle(ColorCreatedEvent message)
        {
            Debug.WriteLine("Event handled in ColorCreatedEventHandler 2");

            return Task.CompletedTask;
        }
    }
}