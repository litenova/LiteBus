using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;

namespace LiteBus.Events
{
    /// <inheritdoc cref="IEventMediator" />
    public class EventMediator : IEventPublisher
    {
        private readonly IMessageMediator _messageMediator;

        public EventMediator(IMessageMediator messageMediator)
        {
            _messageMediator = messageMediator;
        }

        public virtual Task PublishAsync<TEvent>(TEvent @event, 
                                                 CancellationToken cancellationToken = default) 
            where TEvent : IEvent
        {
            throw new NotImplementedException();
        }
    }
}