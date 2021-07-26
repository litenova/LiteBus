using System;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection
{
    internal class EventsModule : IModule
    {
        private readonly Action<LiteBusEventBuilder> _builder;

        public EventsModule(Action<LiteBusEventBuilder> builder)
        {
            _builder = builder;
        }

        public void Build(IServiceCollection services, IMessageRegistry messageRegistry)
        {
            _builder(new LiteBusEventBuilder(messageRegistry));
            
            services.TryAddTransient<IEventMediator, EventMediator>();
            services.TryAddTransient<IEventPublisher, EventMediator>();
        }
    }
}