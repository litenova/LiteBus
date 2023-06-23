using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Mediator;
using LiteBus.Messaging.Internal.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

internal class ModuleRegistry : IModuleRegistry
{
    private readonly HashSet<IModule> _modules = new();
    private readonly IServiceCollection _services;

    public ModuleRegistry(IServiceCollection services)
    {
        _services = services;
    }

    public IModuleRegistry Register(IModule module)
    {
        _modules.Add(module);

        return this;
    }

    public void Initialize()
    {
        var messageRegistry = new MessageRegistry();
        var moduleConfiguration = new ModuleConfiguration(_services, messageRegistry);

        foreach (var module in _modules)
        {
            module.Build(moduleConfiguration);
        }

        _services.TryAddTransient<IMessageMediator, MessageMediator>();
        _services.TryAddSingleton<IMessageRegistry>(messageRegistry);

        foreach (var descriptor in messageRegistry)
        {
            foreach (var handlerDescriptor in descriptor.Handlers)
            {
                _services.TryAddTransient(handlerDescriptor.HandlerType);
            }

            foreach (var handlerDescriptor in descriptor.IndirectHandlers)
            {
                _services.TryAddTransient(handlerDescriptor.HandlerType);
            }

            foreach (var postHandleDescriptor in descriptor.PostHandlers)
            {
                _services.TryAddTransient(postHandleDescriptor.PostHandlerType);
            }

            foreach (var postHandleDescriptor in descriptor.IndirectPostHandlers)
            {
                _services.TryAddTransient(postHandleDescriptor.PostHandlerType);
            }

            foreach (var preHandleDescriptor in descriptor.PreHandlers)
            {
                _services.TryAddTransient(preHandleDescriptor.PreHandlerType);
            }

            foreach (var preHandleDescriptor in descriptor.IndirectPreHandlers)
            {
                _services.TryAddTransient(preHandleDescriptor.PreHandlerType);
            }

            foreach (var errorHandlerDescriptor in descriptor.ErrorHandlers)
            {
                _services.TryAddTransient(errorHandlerDescriptor.ErrorHandlerType);
            }

            foreach (var errorHandlerDescriptor in descriptor.IndirectErrorHandlers)
            {
                _services.TryAddTransient(errorHandlerDescriptor.ErrorHandlerType);
            }
        }
    }
}