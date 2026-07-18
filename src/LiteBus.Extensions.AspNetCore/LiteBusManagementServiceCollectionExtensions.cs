using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Registers LiteBus management endpoint options with the ASP.NET Core service collection.
/// </summary>
public static class LiteBusManagementServiceCollectionExtensions
{
    /// <summary>
    ///     Adds <see cref="LiteBusManagementOptions" /> to the service collection.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configure">An optional callback that configures management endpoint options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLiteBusManagement(
        this IServiceCollection services,
        Action<LiteBusManagementOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is null)
        {
            services.AddSingleton(new LiteBusManagementOptions());
            return services;
        }

        var options = new LiteBusManagementOptions();
        configure(options);
        services.AddSingleton(options);
        return services;
    }
}