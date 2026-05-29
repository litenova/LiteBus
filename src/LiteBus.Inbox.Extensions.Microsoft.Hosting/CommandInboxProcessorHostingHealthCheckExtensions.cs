using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LiteBus.Inbox.Extensions.Microsoft.Hosting;

/// <summary>
///     Registers LiteBus command inbox processor health checks with Microsoft health-check hosting.
/// </summary>
public static class CommandInboxProcessorHostingHealthCheckExtensions
{
    /// <summary>
    ///     Adds a health check that reports command inbox processor host state.
    /// </summary>
    /// <param name="builder">The health-check builder provided by the application host.</param>
    /// <param name="name">The optional health-check name. Defaults to <c>litebus.inbox.processor</c>.</param>
    /// <returns>The current health-check builder.</returns>
    public static IHealthChecksBuilder AddLiteBusCommandInboxProcessor(
        this IHealthChecksBuilder builder,
        string name = "litebus.inbox.processor")
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddCheck<CommandInboxProcessorHealthCheck>(
            name,
            failureStatus: HealthStatus.Unhealthy,
            tags: ["litebus", "inbox", "processor"]);
    }
}
