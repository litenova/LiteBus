using System.Threading.Tasks;
using LightBus.WebApi.Crqs;
using Microsoft.AspNetCore.Mvc;
using LightBus.Commands.Abstractions;
using LightBus.Events.Abstractions;
using LightBus.Queries.Abstractions;

namespace LightBus.WebApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ColorController : ControllerBase
    {
        private readonly ICommandMediator _commandMediator;
        private readonly IEventMediator _eventMediator;
        private readonly IQueryMediator _queryMediator;

        public ColorController(ICommandMediator commandMediator,
                               IQueryMediator queryMediator,
                               IEventMediator eventMediator)
        {
            _commandMediator = commandMediator;
            _queryMediator = queryMediator;
            _eventMediator = eventMediator;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var result = await _queryMediator.QueryAsync(new ColorQuery());

            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> Post(string colorName)
        {
            await _commandMediator.SendAsync(new CreateColorCommand
            {
                ColorName = colorName
            });

            await _eventMediator.PublishAsync(new ColorCreatedEvent
            {
                ColorName = colorName
            });

            return Ok();
        }
    }
}