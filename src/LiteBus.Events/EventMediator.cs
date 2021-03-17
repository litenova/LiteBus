using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;
using LiteBus.Registry.Abstractions;

namespace LiteBus.Events
{
    /// <inheritdoc cref="IEventMediator" />
    public class EventMediator : IEventPublisher
    {
        private readonly IMessageRegistry _messageRegistry;
        private readonly IServiceProvider _serviceProvider;

        public EventMediator(IServiceProvider serviceProvider,
                             IMessageRegistry messageRegistry)
        {
            _serviceProvider = serviceProvider;
            _messageRegistry = messageRegistry;
        }

        public virtual Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : IEvent
        {
            var descriptor = _messageRegistry.GetDescriptor<TEvent>();

            var handlers = _serviceProvider.GetHandlers<TEvent, Task>(descriptor.HandlerTypes);

            return Task.WhenAll(handlers.Select(h => h.HandleAsync(@event, cancellationToken)));
        }
    }
}