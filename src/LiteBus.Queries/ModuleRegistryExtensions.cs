using System;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Queries;

/// <summary>
///     Provides extension methods for <see cref="IModuleRegistry" /> to register query-related modules.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Registers a query module with the specified configuration, automatically ensuring
    ///     that the required <see cref="MessageModule" /> is registered first.
    /// </summary>
    /// <param name="moduleRegistry">The module registry to register the query module with.</param>
    /// <param name="builderAction">An action to configure the query module builder.</param>
    /// <returns>The current <see cref="IModuleRegistry" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="moduleRegistry" /> or <paramref name="builderAction" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     This method automatically registers a <see cref="MessageModule" /> with default configuration
    ///     if one has not already been registered. This ensures that the core messaging services
    ///     (such as <see cref="IMessageMediator" /> and <see cref="IMessageRegistry" />) are available
    ///     for query processing.
    ///     If you need custom configuration for the <see cref="MessageModule" />, register it explicitly
    ///     before calling this method.
    /// </remarks>
    /// <example>
    ///     <code>
    /// // Automatic MessageModule registration with default configuration
    /// services.AddLiteBus(modules =>
    /// {
    ///     modules.AddQueryModule(qry => 
    ///     {
    ///         qry.RegisterFromAssembly(typeof(MyQuery).Assembly);
    ///     });
    /// });
    /// 
    /// // Explicit MessageModule configuration
    /// services.AddLiteBus(modules =>
    /// {
    ///     modules.AddMessageModule(msg => { /* custom config */ });
    ///     modules.AddQueryModule(qry => 
    ///     {
    ///         qry.RegisterFromAssembly(typeof(MyQuery).Assembly);
    ///     });
    /// });
    /// </code>
    /// </example>
    public static IModuleRegistry AddQueryModule(this IModuleRegistry moduleRegistry, Action<QueryModuleBuilder> builderAction)
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

        moduleRegistry.Register(new QueryModule(builderAction));
        return moduleRegistry;
    }
}