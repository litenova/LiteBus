using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using LiteBus.Commands.Abstractions;
using LiteBus.WebApi.Commands;

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
        public Task<string> SendCommandWithResult(CreateNumberCommandWithResult command)
        {
            return _commandMediator.SendAsync(command);
        }

        [HttpPost("without-result")]
        public Task SendCommandWithoutResult(CreateNumberCommand command)
        {
            return _commandMediator.SendAsync(command);
        }
    }
}