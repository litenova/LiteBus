using LiteBus.Events.Abstractions;

namespace LiteBus.WebApi.Events;

public class NumberCreatedEvent : IEvent
{
    public decimal Number { get; set; }
}