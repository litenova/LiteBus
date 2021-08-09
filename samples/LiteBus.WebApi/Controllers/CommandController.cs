﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using LiteBus.Commands.Abstractions;
using LiteBus.WebApi.Commands;
using LiteBus.WebApi.Events;

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

        public Task SendCommandWithoutResult(CreateNumberCommand command)
        {
            return _commandMediator.SendAsync(command);
        }
    }
}