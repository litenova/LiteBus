using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

/// <summary>
///     Represents a module registry responsible for registering and initializing modules and their components.
/// </summary>
internal sealed class ModuleRegistry : IModuleRegistry
{
    private readonly HashSet<IModule> _modules = new();
    private readonly IServiceCollection _services;
    private readonly IMessageRegistry _messageRegistry;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ModuleRegistry" /> class with the provided service collection and message registry.
    /// </summary>
    /// <param name="services">The service collection used for dependency injection.</param>
    /// <param name="messageRegistry">The message registry for handler registration.</param>
    public ModuleRegistry(IServiceCollection services, IMessageRegistry messageRegistry)
    {
        _services = services;
        _messageRegistry = messageRegistry;
    }

    /// <summary>
    ///     Registers a module with the module registry.
    /// </summary>
    /// <param name="module">The module to register.</param>
    /// <returns>The instance of the module registry for method chaining.</returns>
    public IModuleRegistry Register(IModule module)
    {
        _modules.Add(module);
        return this;
    }

    /// <summary>
    ///     Initializes the registered modules and their components.
    /// </summary>
    public void Initialize()
    {
        var moduleConfiguration = new ModuleConfiguration(_services, _messageRegistry);

        foreach (var module in _modules)
        {
            module.Build(moduleConfiguration);
        }

        _services.TryAddTransient<IMessageMediator, MessageMediator>();
        _services.TryAddSingleton<IMessageRegistry>(_messageRegistry);
        _services.TryAddTransient(_ => AmbientExecutionContext.Current);

        foreach (var descriptor in _messageRegistry)
        {
            // Register all handler types from the registry
            RegisterHandlersFromDescriptor(descriptor);
        }
    }

    private void RegisterHandlersFromDescriptor(IMessageDescriptor descriptor)
    {
        // Use a local HashSet to avoid redundant registrations within the same descriptor
        var descriptorHandlerTypes = new HashSet<Type>();

        // Process all handlers first to avoid redundant service registrations
        CollectHandlerTypes(descriptor.Handlers, descriptorHandlerTypes);
        CollectHandlerTypes(descriptor.IndirectHandlers, descriptorHandlerTypes);
        CollectHandlerTypes(descriptor.PreHandlers, descriptorHandlerTypes);
        CollectHandlerTypes(descriptor.IndirectPreHandlers, descriptorHandlerTypes);
        CollectHandlerTypes(descriptor.PostHandlers, descriptorHandlerTypes);
        CollectHandlerTypes(descriptor.IndirectPostHandlers, descriptorHandlerTypes);
        CollectHandlerTypes(descriptor.ErrorHandlers, descriptorHandlerTypes);
        CollectHandlerTypes(descriptor.IndirectErrorHandlers, descriptorHandlerTypes);

        // Register each type once
        foreach (var handlerType in descriptorHandlerTypes)
        {
            // Only register concrete classes with DI container - interfaces and abstract classes are kept in 
            // LiteBus registry for polymorphic dispatch but cannot be instantiated by the DI container.
            // Without this filter, DI would throw "Cannot instantiate implementation type" errors.
            if (handlerType is { IsClass: true, IsAbstract: false })
            {
                _services.TryAddTransient(handlerType);
            }

            _services.TryAddTransient(handlerType);
        }
    }

    private static void CollectHandlerTypes(IEnumerable<IHandlerDescriptor> descriptors, HashSet<Type> handlerTypes)
    {
        foreach (var descriptor in descriptors)
        {
            handlerTypes.Add(descriptor.HandlerType);
        }
    }
}