using System;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection
{
    internal class EventsModule : ILiteBusModule
    {
        private readonly Action<LiteBusEventsConfiguration> _builder;

        public EventsModule(Action<LiteBusEventsConfiguration> builder)
        {
            _builder = builder;
        }

        public void Build(ILiteBusModuleConfiguration configuration)
        {
            _builder(new LiteBusEventsConfiguration(configuration.MessageRegistry));

            configuration.Services.TryAddTransient<IEventMediator, EventMediator>();
            configuration.Services.TryAddTransient<IEventPublisher, EventMediator>();
        }
    }
}