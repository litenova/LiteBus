using System;
using System.Threading;
using System.Threading.Tasks;
using Paykan.Events.Abstraction;
using Paykan.Registry.Abstractions;

namespace Paykan.Events
{
    public class EventMediator : IEventMediator
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IMessageRegistry _messageRegistry;

        public EventMediator(IServiceProvider serviceProvider,
                             IMessageRegistry messageRegistry)
        {
            _serviceProvider = serviceProvider;
            _messageRegistry = messageRegistry;
        }

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : IEvent
        {
            var descriptor = _messageRegistry.GetDescriptor<TEvent>();

            var handlers = _serviceProvider.GetHandlers<TEvent, Task>(descriptor.HandlerTypes);

            return Task.WhenAll(handlers.Select(h => h.HandleAsync(@event, cancellationToken)));
        }
    }
}