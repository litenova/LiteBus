using System;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Queries;

/// <summary>
///     Provides extension methods for <see cref="IModuleRegistry" /> to register query-related modules.
/// </summary>
public static class ModuleRegistryExtensions
{
    /// <summary>
    ///     Adds query mediation to a LiteBus composition.
    /// </summary>
    /// <param name="builder">The package-neutral LiteBus builder.</param>
    /// <param name="builderAction">The query registration callback.</param>
    /// <returns>The current LiteBus builder.</returns>
    public static ILiteBusBuilder AddQueries(
        this ILiteBusBuilder builder,
        Action<QueryModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(builderAction);

        builder.Modules.AddQueryModule(builderAction);
        return builder;
    }

    /// <summary>
    ///     Registers a query module with the specified configuration, or configures the registered one again.
    /// </summary>
    /// <param name="moduleRegistry">The module registry to register the query module with.</param>
    /// <param name="builderAction">An action to configure the query module builder.</param>
    /// <returns>The current <see cref="IModuleRegistry" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="moduleRegistry" /> or <paramref name="builderAction" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     <see cref="QueryModule" /> declares <see cref="IRequires{TModule}" /> for <see cref="MessageModule" />.
    ///     The complete module graph validates that dependency independent of registration order. Calling this twice
    ///     configures one module instead of registering a second one, so several application modules can add to one
    ///     composition.
    /// </remarks>
    /// <example>
    ///     <code>
    /// services.AddLiteBus(builder =>
    /// {
    ///     builder.AddQueries(qry =>
    ///     {
    ///         qry.RegisterFromAssembly(typeof(MyQuery).Assembly);
    ///     });
    ///     builder.AddMessaging(msg => { /* optional core config */ });
    /// });
    /// </code>
    /// </example>
    public static IModuleRegistry AddQueryModule(this IModuleRegistry moduleRegistry, Action<QueryModuleBuilder> builderAction)
    {
        ArgumentNullException.ThrowIfNull(moduleRegistry);
        ArgumentNullException.ThrowIfNull(builderAction);

        var existing = moduleRegistry.Find<QueryModule>();

        if (existing is not null)
        {
            existing.Configure(builderAction, child => moduleRegistry.Register(child));
            return moduleRegistry;
        }

        moduleRegistry.Register(new QueryModule(builderAction));
        return moduleRegistry;
    }
}
