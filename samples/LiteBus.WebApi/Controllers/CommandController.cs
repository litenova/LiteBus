using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.WebApi.Commands;
using LiteBus.WebApi.CommandsWithError;
using LiteBus.WebApi.GenericCommands;
using Microsoft.AspNetCore.Mvc;

namespace LiteBus.WebApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CommandController : ControllerBase
    {
        private readonly ICommandMediator _commandMediator;

        public CommandController(ICommandMediator commandMediator)
        {
            _commandMediator = commandMediator;
        }

        [HttpPost("with-result")]
        public async Task<string> SendCommandWithResult(CreateNumberCommandWithResult command)
        {
            return await _commandMediator.SendAsync(command);
        }

        [HttpPost("without-result")]
        public Task SendCommandWithoutResult(CreateNumberCommand command)
        {
            return _commandMediator.SendAsync(command);
        }
        
        [HttpPost("generic")]
        public async Task<IActionResult> SendGenericCommand()
        {
            await _commandMediator.SendAsync(new CreateVehicleCommand<string>());

            return Ok();
        }
        
        [HttpPost("error")]
        public async Task<IActionResult> SendErrorCommand()
        {
            await _commandMediator.SendAsync(new ECommand());

            return Ok();
        }
    }
}