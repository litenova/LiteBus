using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.WebApi.Crqs;
using Microsoft.AspNetCore.Mvc;

namespace LiteBus.WebApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class InheritanceController : ControllerBase
    {
        private readonly ICommandMediator _commandMediator;

        public InheritanceController(ICommandMediator commandMediator)
        {
            _commandMediator = commandMediator;
        }

        [HttpPost("create1")]
        public async Task<IActionResult> Post()
        {
            CreatePersonCommand command = new CreateSoldierCommand();

            await _commandMediator.SendAsync(command);

            return Ok();
        }
        
        [HttpPost("create2")]
        public async Task<IActionResult> Post1()
        {
            CreatePersonCommand command = new CreatePersonCommand();

            await _commandMediator.SendAsync(command);

            return Ok();
        }
    }
}