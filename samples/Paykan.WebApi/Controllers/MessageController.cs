using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Paykan.Abstractions;
using Paykan.Messaging.Abstractions;
using Paykan.WebApi.Crqs;

namespace Paykan.WebApi.Controllers
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