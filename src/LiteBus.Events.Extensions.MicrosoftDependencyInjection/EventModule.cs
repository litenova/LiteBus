using System;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection;

internal class EventModule : IModule
{
    private readonly Action<EventModuleBuilder> _builder;

    public EventModule(Action<EventModuleBuilder> builder)
    {
        _builder = builder;
    }

    public void Build(ILiteBusModuleConfiguration configuration)
    {
        _builder(new EventModuleBuilder(configuration.MessageRegistry));

        configuration.Services.TryAddTransient<IEventMediator, EventMediator>();
        configuration.Services.TryAddTransient<IEventPublisher, EventMediator>();
    }
}