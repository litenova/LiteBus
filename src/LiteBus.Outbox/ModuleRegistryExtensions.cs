using System;
using LiteBus.Messaging;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Provides extension methods for registering outbox modules.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Adds durable outbox processing to a LiteBus composition.
    /// </summary>
    /// <param name="builder">The package-neutral LiteBus builder.</param>
    /// <param name="builderAction">The outbox configuration callback.</param>
    /// <returns>The current LiteBus builder.</returns>
    public static ILiteBusBuilder AddOutbox(
        this ILiteBusBuilder builder,
        Action<OutboxModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(builderAction);

        builder.Modules.AddOutboxModule(builderAction);
        return builder;
    }

    /// <summary>
    ///     Registers the outbox module.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <param name="builderAction">The outbox module configuration action.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>
    ///     <see cref="OutboxModule" /> declares its messaging dependency in the module graph, so registration order does
    ///     not affect validation.
    /// </remarks>
    public static IModuleRegistry AddOutboxModule(this IModuleRegistry moduleRegistry, Action<OutboxModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        moduleRegistry.Register(new OutboxModule(builderAction));
        return moduleRegistry;
    }

    /// <summary>
    ///     Registers the outbox module with default settings.
    /// </summary>
    /// <param name="moduleRegistry">The module registry.</param>
    /// <returns>The current module registry.</returns>
    /// <remarks>The complete graph validates the required messaging module.</remarks>
    public static IModuleRegistry AddOutboxModule(this IModuleRegistry moduleRegistry)
    {
        return moduleRegistry.AddOutboxModule(_ =>
        {
        });
    }
}
