using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
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


    }
}