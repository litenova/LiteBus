using System.Collections.Generic;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.FindStrategies;
using LiteBus.Messaging.Abstractions.MediationStrategies;
using LiteBus.WebApi.PlainMessages;
using LiteBus.WebApi.Queries;
using Microsoft.AspNetCore.Mvc;

namespace LiteBus.WebApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PlainMessageController
    {
        private readonly IMessageMediator _messageMediator;

        public PlainMessageController(IMessageMediator messageMediator)
        {
            _messageMediator = messageMediator;
        }
        
        [HttpPost]
        public async Task GetNumbers()
        {
            var message = new PlainMessage();
            var resolveStrategy = new ActualTypeOrBaseTypeMessageResolveStrategy();
            var mediationStrategy = new AsyncBroadcastMediationStrategy<PlainMessage>(default);
            
            await _messageMediator.Mediate(message, resolveStrategy, mediationStrategy);
        }
    }
}