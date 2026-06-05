using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LiteBus.Extensions.Microsoft.HealthChecks;

/// <summary>
///     Registers LiteBus inbox and outbox health checks with ASP.NET Core health check hosting.
/// </summary>
public static class LiteBusHealthChecksExtensions
{
    /// <summary>
    ///     Gets the default inbox health check name.
    /// </summary>
    public const string DefaultInboxHealthCheckName = "litebus.inbox";

    /// <summary>
    ///     Gets the default outbox health check name.
    /// </summary>
    public const string DefaultOutboxHealthCheckName = "litebus.outbox";

    /// <summary>
    ///     Adds an inbox queue health check backed by <see cref="LiteBus.Inbox.Abstractions.IInboxDiagnosticsStore" />.
    /// </summary>
    /// <param name="builder">The health checks builder receiving the registration.</param>
    /// <param name="name">The health check name reported to hosts and probes.</param>
    /// <param name="configureOptions">An optional callback that configures dead-letter thresholds.</param>
    /// <returns>The health checks builder for method chaining.</returns>
    public static IHealthChecksBuilder AddLiteBusInboxHealthCheck(
        this IHealthChecksBuilder builder,
        string name = DefaultInboxHealthCheckName,
        Action<LiteBusInboxHealthCheckOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Configure<LiteBusInboxHealthCheckOptions>(options =>
        {
            configureOptions?.Invoke(options);
        });

        return builder.AddCheck<InboxHealthCheck>(name);
    }

    /// <summary>
    ///     Adds an outbox queue health check backed by <see cref="LiteBus.Outbox.Abstractions.IOutboxDiagnosticsStore" />.
    /// </summary>
    /// <param name="builder">The health checks builder receiving the registration.</param>
    /// <param name="name">The health check name reported to hosts and probes.</param>
    /// <param name="configureOptions">An optional callback that configures dead-letter thresholds.</param>
    /// <returns>The health checks builder for method chaining.</returns>
    public static IHealthChecksBuilder AddLiteBusOutboxHealthCheck(
        this IHealthChecksBuilder builder,
        string name = DefaultOutboxHealthCheckName,
        Action<LiteBusOutboxHealthCheckOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Configure<LiteBusOutboxHealthCheckOptions>(options =>
        {
            configureOptions?.Invoke(options);
        });

        return builder.AddCheck<OutboxHealthCheck>(name);
    }
}
