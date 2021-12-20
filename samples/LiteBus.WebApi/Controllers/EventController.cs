using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.WebApi.Events;
using Microsoft.AspNetCore.Mvc;

namespace LiteBus.WebApi.Controllers;

[ApiController]
[Route("[controller]")]
public class EventController : ControllerBase
{
    private readonly IEventMediator _eventMediator;

    public EventController(IEventMediator eventMediator)
    {
        _eventMediator = eventMediator;
    }

    [HttpPost]
    public Task PublishEvent(NumberCreatedEvent @event)
    {
        return _eventMediator.PublishAsync(@event);
    }
}