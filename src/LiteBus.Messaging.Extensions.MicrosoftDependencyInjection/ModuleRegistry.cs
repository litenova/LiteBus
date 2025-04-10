using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Mediator;
using LiteBus.Messaging.Internal.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

/// <summary>
/// Represents a module registry responsible for registering and initializing modules and their components.
/// </summary>
internal sealed class ModuleRegistry : IModuleRegistry
{
    private readonly HashSet<IModule> _modules = new();
    private readonly IServiceCollection _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleRegistry"/> class with the provided service collection.
    /// </summary>
    /// <param name="services">The service collection used for dependency injection.</param>
    public ModuleRegistry(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Registers a module with the module registry.
    /// </summary>
    /// <param name="module">The module to register.</param>
    /// <returns>The instance of the module registry for method chaining.</returns>
    public IModuleRegistry Register(IModule module)
    {
        _modules.Add(module);
        return this;
    }

    /// <summary>
    /// Initializes the registered modules and their components.
    /// </summary>
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
        _services.TryAddTransient(_ => AmbientExecutionContext.Current);

        foreach (var descriptor in messageRegistry)
        {
            // Register handlers, post handlers, pre handlers, and error handlers for message descriptors.
            foreach (var handlerDescriptor in descriptor.Handlers.Concat(descriptor.IndirectHandlers))
            {
                _services.TryAddTransient(handlerDescriptor.HandlerType);
            }

            foreach (var postHandleDescriptor in descriptor.PostHandlers.Concat(descriptor.IndirectPostHandlers))
            {
                _services.TryAddTransient(postHandleDescriptor.HandlerType);
            }

            foreach (var preHandleDescriptor in descriptor.PreHandlers.Concat(descriptor.IndirectPreHandlers))
            {
                _services.TryAddTransient(preHandleDescriptor.HandlerType);
            }

            foreach (var errorHandlerDescriptor in descriptor.ErrorHandlers.Concat(descriptor.IndirectErrorHandlers))
            {
                _services.TryAddTransient(errorHandlerDescriptor.HandlerType);
            }
        }
    }
}