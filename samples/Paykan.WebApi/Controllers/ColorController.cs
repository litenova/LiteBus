using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Paykan.Abstractions;
using Paykan.WebApi.Crqs;

namespace Paykan.WebApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ColorController : ControllerBase
    {
        private readonly ICommandMediator _commandMediator;
        private readonly IQueryMediator _queryMediator;
        private readonly IEventMediator _eventMediator;

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