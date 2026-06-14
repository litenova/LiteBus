using System;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Events;

/// <summary>
///     Provides extension methods for <see cref="IModuleRegistry" /> to register event-related modules.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers an event module with the specified configuration, automatically ensuring
    ///     that the required <see cref="MessageModule" /> is registered first.
    /// </summary>
    /// <param name="moduleRegistry">The module registry to register the event module with.</param>
    /// <param name="builderAction">An action to configure the event module builder.</param>
    /// <returns>The current <see cref="IModuleRegistry" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="moduleRegistry" /> or <paramref name="builderAction" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     Register <see cref="MessageModule" /> before calling this method. The message module provides core messaging
    ///     services (such as <see cref="IMessageMediator" /> and <see cref="IMessageRegistry" />) required for event
    ///     processing.
    /// </remarks>
    /// <example>
    ///     <code>
    /// services.AddLiteBus(registry =>
    /// {
    ///     registry.AddMessageModule(msg => { /* optional core config */ });
    ///     registry.AddEventModule(evt => 
    ///     {
    ///         evt.RegisterFromAssembly(typeof(MyEvent).Assembly);
    ///     });
    /// });
    /// </code>
    /// </example>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when <see cref="MessageModule" /> has not been registered.
    /// </exception>
    public static IModuleRegistry AddEventModule(this IModuleRegistry moduleRegistry, Action<EventModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        if (!moduleRegistry.IsModuleRegistered<MessageModule>())
        {
            throw new LiteBusConfigurationException(
                "MessageModule must be registered before AddEventModule(). Call AddMessageModule() first.");
        }

        moduleRegistry.Register(new EventModule(builderAction));
        return moduleRegistry;
    }

    /// <summary>
    ///     Registers an event module with default configuration, automatically ensuring
    ///     that the required <see cref="MessageModule" /> is registered first.
    /// </summary>
    /// <param name="moduleRegistry">The module registry to register the event module with.</param>
    /// <returns>The current <see cref="IModuleRegistry" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="moduleRegistry" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     Register <see cref="MessageModule" /> before calling this method. The event module is registered with default
    ///     settings.
    /// </remarks>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when <see cref="MessageModule" /> has not been registered.
    /// </exception>
    /// <example>
    ///     <code>
    /// // Simple registration with default configuration
    /// services.AddLiteBus(registry =>
    /// {
    ///     registry.AddMessageModule(_ => { });
    ///     registry.AddEventModule();
    /// });
    /// </code>
    /// </example>
    public static IModuleRegistry AddEventModule(this IModuleRegistry moduleRegistry)
    {
        return moduleRegistry.AddEventModule(_ =>
        {
        });
    }
}