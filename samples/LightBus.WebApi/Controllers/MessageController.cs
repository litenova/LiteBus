using System;
using System.Threading.Tasks;
using LightBus.WebApi.Crqs;
using Microsoft.AspNetCore.Mvc;
using LightBus.Messaging.Abstractions;

namespace LightBus.WebApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class MessageController : ControllerBase
    {
        private readonly IMessageMediator _messageMediator;

        public MessageController(IMessageMediator messageMediator)
        {
            _messageMediator = messageMediator;
        }

        [HttpGet]
        public async Task<IActionResult> SimpleMessageWithOneHandler()
        {
            var number = new Random().Next();

            var result = await _messageMediator.SendAsync<Task<int>>(new PlainMessage
            {
                Number = number
            });

            return Ok(result);
        }
    }
}