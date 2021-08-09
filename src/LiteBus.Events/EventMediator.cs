using System;
using System.Threading;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using MorseCode.ITask;

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

        public virtual ITask PublishAsync<TEvent>(TEvent @event, 
                                                 CancellationToken cancellationToken = default) 
            where TEvent : IEvent
        {
            throw new NotImplementedException();
        }
    }
}