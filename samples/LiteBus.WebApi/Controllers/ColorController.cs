using Microsoft.AspNetCore.Mvc;
using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.WebApi.Commands;
using LiteBus.WebApi.Events;
using LiteBus.WebApi.Queries;
using MorseCode.ITask;

namespace LiteBus.WebApi.Controllers
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
        public async ITask<IActionResult> Get()
        {
            var result = await _queryMediator.QueryAsync(new ColorQuery());

            return Ok(result);
        }

        [HttpPost("without-result")]
        public async ITask<IActionResult> Post(string colorName)
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

        [HttpPost("with-result")]
        public async ITask<IActionResult> Post2(string colorName)
        {
            var result = await _commandMediator.SendAsync<bool>(new CreateColorCommandWithResult
            {
                ColorName = colorName
            });

            await _eventMediator.PublishAsync(new ColorCreatedEvent
            {
                ColorName = colorName
            });

            return Ok(result);
        }
    }
}