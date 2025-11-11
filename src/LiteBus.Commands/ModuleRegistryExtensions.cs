using System;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Commands;

/// <summary>
///     Provides extension methods for <see cref="IModuleRegistry" /> to register command-related modules.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers a command module with the specified configuration, automatically ensuring
    ///     that the required <see cref="MessageModule" /> is registered first.
    /// </summary>
    /// <param name="moduleRegistry">The module registry to register the command module with.</param>
    /// <param name="builderAction">An action to configure the command module builder.</param>
    /// <returns>The current <see cref="IModuleRegistry" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="moduleRegistry" /> or <paramref name="builderAction" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     This method automatically registers a <see cref="MessageModule" /> with default configuration
    ///     if one has not already been registered. This ensures that the core messaging services
    ///     (such as <see cref="IMessageMediator" /> and <see cref="IMessageRegistry" />) are available
    ///     for command processing.
    ///     If you need custom configuration for the <see cref="MessageModule" />, register it explicitly
    ///     before calling this method.
    /// </remarks>
    /// <example>
    ///     <code>
    /// // Automatic MessageModule registration with default configuration
    /// services.AddLiteBus(modules =>
    /// {
    ///     modules.AddCommandModule(cmd => 
    ///     {
    ///         cmd.RegisterFromAssembly(typeof(MyCommand).Assembly);
    ///     });
    /// });
    /// 
    /// // Explicit MessageModule configuration
    /// services.AddLiteBus(modules =>
    /// {
    ///     modules.AddMessageModule(msg => { /* custom config */ });
    ///     modules.AddCommandModule(cmd => 
    ///     {
    ///         cmd.RegisterFromAssembly(typeof(MyCommand).Assembly);
    ///     });
    /// });
    /// </code>
    /// </example>
    public static IModuleRegistry AddCommandModule(this IModuleRegistry moduleRegistry, Action<CommandModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        // Ensure MessageModule is registered first with default configuration
        if (!moduleRegistry.IsModuleRegistered<MessageModule>())
        {
            moduleRegistry.Register(new MessageModule(_ =>
            {
            }));
        }

        moduleRegistry.Register(new CommandModule(builderAction));
        return moduleRegistry;
    }
}