using System;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Provides extension methods for <see cref="IModuleRegistry" /> to register messaging-related modules.
/// </summary>
public static class ModuleRegistryExtensions
{
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
    ///     The message module provides core messaging infrastructure and should typically be registered
    ///     before other LiteBus modules (commands, events, queries) that depend on its services.
    /// </remarks>
    /// <example>
    ///     <code>
    /// services.AddLiteBus(modules =>
    /// {
    ///     modules.AddMessageModule(msg => 
    ///     {
    ///         msg.RegisterFromAssembly(typeof(MyHandler).Assembly);
    ///     });
    ///     modules.AddCommandModule(cmd => { /* ... */ });
    ///     modules.AddEventModule(evt => { /* ... */ });
    /// });
    /// </code>
    /// </example>
    public static IModuleRegistry AddMessageModule(this IModuleRegistry moduleRegistry, Action<MessageModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        // Check if MessageModule is already registered
        if (moduleRegistry.IsModuleRegistered<MessageModule>())
        {
            throw new InvalidOperationException(
                "Ensure that AddMessageModule() is called before other LiteBus modules " +
                "(AddCommandModule, AddEventModule, AddQueryModule) to provide the core messaging infrastructure.");
        }

        moduleRegistry.Register(new MessageModule(builderAction));
        return moduleRegistry;
    }
}