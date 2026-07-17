using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Provides extension methods for <see cref="IModuleRegistry" /> to register messaging-related modules.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Adds the messaging core and configures message contracts, serialization, and handlers.
    /// </summary>
    /// <param name="builder">The package-neutral LiteBus builder.</param>
    /// <param name="builderAction">The messaging configuration callback.</param>
    /// <returns>The current LiteBus builder.</returns>
    public static ILiteBusBuilder AddMessaging(
        this ILiteBusBuilder builder,
        Action<MessageModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(builderAction);

        builder.Modules.AddMessageModule(builderAction);
        return builder;
    }

    /// <summary>
    ///     Registers a message module with the specified configuration.
    /// </summary>
    /// <param name="moduleRegistry">The module registry to register the message module with.</param>
    /// <param name="builderAction">An action to configure the message module builder.</param>
    /// <returns>The current <see cref="IModuleRegistry" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="moduleRegistry" /> or <paramref name="builderAction" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     The message module provides core messaging infrastructure. Dependent modules declare that relationship through
    ///     <see cref="IRequires{TModule}" />, so callback order does not affect graph validation.
    /// </remarks>
    /// <example>
    ///     <code>
    /// services.AddLiteBus(registry =>
    /// {
    ///     registry.AddMessageModule(msg => 
    ///     {
    ///         msg.RegisterFromAssembly(typeof(MyHandler).Assembly);
    ///     });
    ///     registry.AddCommandModule(cmd => { /* ... */ });
    ///     registry.AddEventModule(evt => { /* ... */ });
    /// });
    /// </code>
    /// </example>
    public static IModuleRegistry AddMessageModule(this IModuleRegistry moduleRegistry, Action<MessageModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        moduleRegistry.Register(new MessageModule(builderAction));
        return moduleRegistry;
    }
}
