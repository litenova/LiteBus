using System;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LiteBus.Extensions.Diagnostics.HealthChecks;

/// <summary>
///     Registers LiteBus diagnostic probes with ASP.NET Core health checks.
/// </summary>
public static class LiteBusHealthCheckExtensions
{
    /// <summary>
    ///     Adds a health check that runs probes registered on <see cref="LiteBusHostManifest" />.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check name. The default is <c>litebus</c>.</param>
    /// <returns>The health checks builder for chaining.</returns>
    public static IHealthChecksBuilder AddLiteBus(
        this IHealthChecksBuilder builder,
        string name = "litebus")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return builder.AddCheck<LiteBusHealthCheck>(
            name,
            failureStatus: HealthStatus.Unhealthy,
            tags: ["litebus"]);
    }
}
