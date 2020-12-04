using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BasicBus.Abstractions;
using BasicBus.WebApi.Commands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BasicBus.WebApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing",
            "Bracing",
            "Chilly",
            "Cool",
            "Mild",
            "Warm",
            "Balmy",
            "Hot",
            "Sweltering",
            "Scorching"
        };

        private readonly ILogger<WeatherForecastController> _logger;
        private readonly ICommandMediator _commandMediator;

        public WeatherForecastController(ILogger<WeatherForecastController> logger, 
                                         ICommandMediator commandMediator)
        {
            _logger = logger;
            _commandMediator = commandMediator;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            await _commandMediator.SendAsync(new CreateUserCommand
            {
                Name = "Hello"
            });

            return Ok();
        }
    }
}