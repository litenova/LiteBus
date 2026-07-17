using System;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Events;

/// <summary>
///     Provides extension methods for <see cref="IModuleRegistry" /> to register event-related modules.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Adds event mediation to a LiteBus composition.
    /// </summary>
    /// <param name="builder">The package-neutral LiteBus builder.</param>
    /// <param name="builderAction">The event registration callback.</param>
    /// <returns>The current LiteBus builder.</returns>
    public static ILiteBusBuilder AddEvents(
        this ILiteBusBuilder builder,
        Action<EventModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(builderAction);

        builder.Modules.AddEventModule(builderAction);
        return builder;
    }

    /// <summary>
    ///     Registers an event module with the specified configuration.
    /// </summary>
    /// <param name="moduleRegistry">The module registry to register the event module with.</param>
    /// <param name="builderAction">An action to configure the event module builder.</param>
    /// <returns>The current <see cref="IModuleRegistry" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="moduleRegistry" /> or <paramref name="builderAction" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     <see cref="EventModule" /> declares <see cref="IRequires{TModule}" /> for <see cref="MessageModule" />.
    ///     The complete module graph validates that dependency independent of registration order.
    /// </remarks>
    /// <example>
    ///     <code>
    /// services.AddLiteBus(registry =>
    /// {
    ///     registry.AddEventModule(evt => 
    ///     {
    ///         evt.RegisterFromAssembly(typeof(MyEvent).Assembly);
    ///     });
    ///     registry.AddMessageModule(msg => { /* optional core config */ });
    /// });
    /// </code>
    /// </example>
    public static IModuleRegistry AddEventModule(this IModuleRegistry moduleRegistry, Action<EventModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        moduleRegistry.Register(new EventModule(builderAction));
        return moduleRegistry;
    }

    /// <summary>
    ///     Registers an event module with default configuration.
    /// </summary>
    /// <param name="moduleRegistry">The module registry to register the event module with.</param>
    /// <returns>The current <see cref="IModuleRegistry" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="moduleRegistry" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     The complete module graph validates the event module's messaging dependency independent of registration order.
    /// </remarks>
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
