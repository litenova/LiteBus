using System;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

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
    ///     Register <see cref="MessageModule" /> before calling this method. The message module provides core messaging
    ///     services (such as <see cref="IMessageMediator" /> and <see cref="IMessageRegistry" />) required for query
    ///     processing.
    /// </remarks>
    /// <example>
    ///     <code>
    /// services.AddLiteBus(registry =>
    /// {
    ///     registry.AddMessageModule(msg => { /* optional core config */ });
    ///     registry.AddQueryModule(qry => 
    ///     {
    ///         qry.RegisterFromAssembly(typeof(MyQuery).Assembly);
    ///     });
    /// });
    /// </code>
    /// </example>
    /// <exception cref="LiteBusConfigurationException">
    ///     Thrown when <see cref="MessageModule" /> has not been registered.
    /// </exception>
    public static IModuleRegistry AddQueryModule(this IModuleRegistry moduleRegistry, Action<QueryModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        if (!moduleRegistry.IsModuleRegistered<MessageModule>())
        {
            throw new LiteBusConfigurationException(
                "MessageModule must be registered before AddQueryModule(). Call AddMessageModule() first.");
        }

        moduleRegistry.Register(new QueryModule(builderAction));
        return moduleRegistry;
    }
}