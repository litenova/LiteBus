using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LiteBus.Outbox.Extensions.Microsoft.Hosting;

/// <summary>
///     Registers LiteBus outbox processor health checks with Microsoft health-check hosting.
/// </summary>
public static class OutboxProcessorHostingHealthCheckExtensions
{
    /// <summary>
    ///     Adds a health check that reports outbox processor host state.
    /// </summary>
    /// <param name="builder">The health-check builder provided by the application host.</param>
    /// <param name="name">The optional health-check name. Defaults to <c>litebus.outbox.processor</c>.</param>
    /// <returns>The current health-check builder.</returns>
    public static IHealthChecksBuilder AddLiteBusOutboxProcessor(
        this IHealthChecksBuilder builder,
        string name = "litebus.outbox.processor")
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddCheck<OutboxProcessorHealthCheck>(
            name,
            failureStatus: HealthStatus.Unhealthy,
            tags: ["litebus", "outbox", "processor"]);
    }
}
