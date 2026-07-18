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
    /// <param name="configure">Optional callback to configure probe execution and zero-probe policy.</param>
    /// <param name="name">The health check name. The default is <c>litebus</c>.</param>
    /// <returns>The health checks builder for chaining.</returns>
    public static IHealthChecksBuilder AddLiteBus(
        this IHealthChecksBuilder builder,
        Action<LiteBusHealthCheckOptions>? configure = null,
        string name = "litebus")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var options = new LiteBusHealthCheckOptions();
        configure?.Invoke(options);
        ArgumentNullException.ThrowIfNull(options.DiagnosticChecks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.DiagnosticChecks.MaxParallelism);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.DiagnosticChecks.Timeout.Ticks);
        builder.Services.AddSingleton(options);

        return builder.AddCheck<LiteBusHealthCheck>(
            name,
            HealthStatus.Unhealthy,
            ["litebus", "ready"]);
    }
}
