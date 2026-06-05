using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LiteBus.Extensions.Microsoft.HealthChecks;

/// <summary>
///     Reports outbox queue depth and dead-letter growth through <see cref="IOutboxDiagnosticsStore" />.
/// </summary>
public sealed class OutboxHealthCheck : IHealthCheck
{
    /// <summary>
    ///     Gets the diagnostics store queried for status counts.
    /// </summary>
    private readonly IOutboxDiagnosticsStore _diagnosticsStore;

    /// <summary>
    ///     Gets the dead-letter threshold applied to the health result.
    /// </summary>
    private readonly LiteBusOutboxHealthCheckOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxHealthCheck" /> class.
    /// </summary>
    /// <param name="diagnosticsStore">The diagnostics store queried for status counts.</param>
    /// <param name="options">The dead-letter threshold applied to the health result.</param>
    public OutboxHealthCheck(IOutboxDiagnosticsStore diagnosticsStore, IOptions<LiteBusOutboxHealthCheckOptions> options)
    {
        _diagnosticsStore = diagnosticsStore ?? throw new ArgumentNullException(nameof(diagnosticsStore));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var statusCounts = await _diagnosticsStore.GetStatusCountsAsync(cancellationToken).ConfigureAwait(false);
            var data = BuildStatusData(statusCounts);
            var deadLetteredCount = statusCounts.TryGetValue(OutboxStatus.DeadLettered, out var count) ? count : 0;

            if (deadLetteredCount > _options.MaxDeadLetterCount)
            {
                return HealthCheckResult.Unhealthy(
                    $"Outbox dead-letter count {deadLetteredCount} exceeds threshold {_options.MaxDeadLetterCount}.",
                    data: data);
            }

            return HealthCheckResult.Healthy("Outbox diagnostics query succeeded.", data);
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Outbox diagnostics query failed.", exception);
        }
    }

    /// <summary>
    ///     Converts status counts into health-check report data.
    /// </summary>
    /// <param name="statusCounts">The status counts returned by the diagnostics store.</param>
    /// <returns>A read-only map keyed by outbox status name.</returns>
    private static IReadOnlyDictionary<string, object> BuildStatusData(IReadOnlyDictionary<OutboxStatus, int> statusCounts)
    {
        var data = new Dictionary<string, object>(statusCounts.Count, StringComparer.Ordinal);

        foreach (var (status, count) in statusCounts)
        {
            data[status.ToString()] = count;
        }

        return data;
    }
}
