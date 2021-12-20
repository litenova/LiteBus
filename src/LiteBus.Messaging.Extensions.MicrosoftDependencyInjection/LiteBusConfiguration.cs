using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Mediator;
using LiteBus.Messaging.Internal.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

internal class LiteBusConfiguration : ILiteBusConfiguration
{
    private readonly HashSet<ILiteBusModule> _modules = new();
    private readonly IServiceCollection _services;

    public LiteBusConfiguration(IServiceCollection services)
    {
        _services = services;
    }

    public ILiteBusConfiguration AddModule(ILiteBusModule liteBusModule)
    {
        _modules.Add(liteBusModule);

        return this;
    }

    public void Initialize()
    {
        var messageRegistry = new MessageRegistry();
        var moduleConfiguration = new LiteBusModuleConfiguration(_services, messageRegistry);

        foreach (var module in _modules)
        {
            module.Build(moduleConfiguration);
        }

        _services.TryAddTransient<IMessageMediator, MessageMediator>();
        _services.TryAddSingleton<IMessageRegistry>(messageRegistry);

        foreach (var descriptor in messageRegistry)
        {
            foreach (var handlerDescriptor in descriptor.HandlerDescriptors)
            {
                _services.TryAddTransient(handlerDescriptor.HandlerType);
            }

            foreach (var postHandleDescriptor in descriptor.PostHandlerDescriptors)
            {
                _services.TryAddTransient(postHandleDescriptor.PostHandlerType);
            }

            foreach (var preHandleDescriptor in descriptor.PreHandlerDescriptors)
            {
                _services.TryAddTransient(preHandleDescriptor.PreHandlerType);
            }

            foreach (var errorHandlerDescriptor in descriptor.ErrorHandlerDescriptors)
            {
                _services.TryAddTransient(errorHandlerDescriptor.ErrorHandlerType);
            }
        }
    }
}