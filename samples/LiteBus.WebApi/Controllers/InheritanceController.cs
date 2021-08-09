using LiteBus.Commands.Abstractions;
using LiteBus.WebApi.Commands;
using Microsoft.AspNetCore.Mvc;
using MorseCode.ITask;

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

        [HttpPost("scenario-1")]
        public async ITask<IActionResult> Post()
        {
            CreatePersonCommand command = new CreateSoldierCommand();

            await _commandMediator.SendAsync(command);

            return Ok();
        }
        
        [HttpPost("scenario-2")]
        public async ITask<IActionResult> Post1()
        {
            CreatePersonCommand command = new CreateDoctorCommand();

            await _commandMediator.SendAsync(command);

            return Ok();
        }
    }
}