using System;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Registers a refusal problem catalog built from the composed refusal registry.
/// </summary>
public static class RefusalProblemCatalogServiceCollectionExtensions
{
    /// <summary>
    ///     Adds the <see cref="IRefusalProblemCatalog" /> built from the composed <see cref="IRefusalCatalog" />.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configure">An optional callback that maps extra HTTP codes on the built catalog.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         The catalog is built when first resolved, from the refusal registry the messaging composition holds,
    ///         so product codes registered on the messaging builder answer as problem details without a second
    ///         registration. The callback maps HTTP-only codes afterwards, such as a prefix the host answers with
    ///         one status whatever the neutral kind says.
    ///     </para>
    ///     <para>
    ///         When the application registered its own catalog, that registration wins and this call adds nothing.
    ///         When messaging was never composed, resolving the catalog throws naming the missing composition
    ///         instead of answering from an empty catalog.
    ///     </para>
    ///     <code><![CDATA[
    /// services.AddRefusalProblemCatalog(catalog =>
    /// {
    ///     catalog.Map("billing.*", StatusCodes.Status402PaymentRequired, "Billing required", "billing");
    /// });
    /// ]]></code>
    /// </remarks>
    public static IServiceCollection AddRefusalProblemCatalog(
        this IServiceCollection services,
        Action<RefusalProblemCatalog>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IRefusalProblemCatalog>(provider =>
        {
            if (provider.GetService(typeof(IRefusalCatalog)) is not IRefusalCatalog refusals)
            {
                throw new InvalidOperationException(
                    "No refusal catalog is composed, so the problem catalog cannot be built from it. "
                    + "Compose messaging with AddMessaging, or register a catalog built by "
                    + "RefusalProblemCatalog.CreateDefault directly.");
            }

            var catalog = RefusalProblemCatalog.CreateFrom(refusals);
            configure?.Invoke(catalog);

            return catalog;
        });

        return services;
    }
}
