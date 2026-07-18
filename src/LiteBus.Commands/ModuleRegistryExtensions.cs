using System;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Commands;

/// <summary>
///     Provides extension methods for <see cref="IModuleRegistry" /> to register command-related modules.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Adds command mediation to a LiteBus composition.
    /// </summary>
    /// <param name="builder">The package-neutral LiteBus builder.</param>
    /// <param name="builderAction">The command registration callback.</param>
    /// <returns>The current LiteBus builder.</returns>
    public static ILiteBusBuilder AddCommands(
        this ILiteBusBuilder builder,
        Action<CommandModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(builderAction);

        builder.Modules.AddCommandModule(builderAction);
        return builder;
    }

    /// <summary>
    ///     Registers a command module with the specified configuration.
    /// </summary>
    /// <param name="moduleRegistry">The module registry to register the command module with.</param>
    /// <param name="builderAction">An action to configure the command module builder.</param>
    /// <returns>The current <see cref="IModuleRegistry" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="moduleRegistry" /> or <paramref name="builderAction" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     <see cref="CommandModule" /> declares <see cref="IRequires{TModule}" /> for <see cref="MessageModule" />.
    ///     The complete module graph validates that dependency independent of registration order.
    /// </remarks>
    /// <example>
    ///     <code>
    /// services.AddLiteBus(builder =>
    /// {
    ///     builder.AddCommands(cmd =>
    ///     {
    ///         cmd.RegisterFromAssembly(typeof(MyCommand).Assembly);
    ///     });
    ///     builder.AddMessaging(msg => { /* optional core config */ });
    /// });
    /// </code>
    /// </example>
    public static IModuleRegistry AddCommandModule(this IModuleRegistry moduleRegistry, Action<CommandModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        moduleRegistry.Register(new CommandModule(builderAction));
        return moduleRegistry;
    }
}
