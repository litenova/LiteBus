using System.Threading;
using System.Threading.Tasks;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.FindStrategies;
using LiteBus.Messaging.Abstractions.MediationStrategies;

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

        public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : IEvent
        {
            var mediationStrategy = new AsyncBroadcastMediationStrategy<TEvent>(cancellationToken);

            var findStrategy = new ActualTypeOrBaseTypeMessageResolveStrategy();

            await _messageMediator.Mediate(@event, findStrategy, mediationStrategy);
        }
    }
}