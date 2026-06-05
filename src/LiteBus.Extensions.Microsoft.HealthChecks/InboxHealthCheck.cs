using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace LiteBus.Extensions.Microsoft.HealthChecks;

/// <summary>
///     Reports inbox queue depth and dead-letter growth through <see cref="IInboxDiagnosticsStore" />.
/// </summary>
public sealed class InboxHealthCheck : IHealthCheck
{
    /// <summary>
    ///     Gets the diagnostics store queried for status counts.
    /// </summary>
    private readonly IInboxDiagnosticsStore _diagnosticsStore;

    /// <summary>
    ///     Gets the dead-letter threshold applied to the health result.
    /// </summary>
    private readonly LiteBusInboxHealthCheckOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxHealthCheck" /> class.
    /// </summary>
    /// <param name="diagnosticsStore">The diagnostics store queried for status counts.</param>
    /// <param name="options">The dead-letter threshold applied to the health result.</param>
    public InboxHealthCheck(IInboxDiagnosticsStore diagnosticsStore, IOptions<LiteBusInboxHealthCheckOptions> options)
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
            var deadLetteredCount = statusCounts.TryGetValue(InboxStatus.DeadLettered, out var count) ? count : 0;

            if (deadLetteredCount > _options.MaxDeadLetterCount)
            {
                return HealthCheckResult.Unhealthy(
                    $"Inbox dead-letter count {deadLetteredCount} exceeds threshold {_options.MaxDeadLetterCount}.",
                    data: data);
            }

            return HealthCheckResult.Healthy("Inbox diagnostics query succeeded.", data);
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Inbox diagnostics query failed.", exception);
        }
    }

    /// <summary>
    ///     Converts status counts into health-check report data.
    /// </summary>
    /// <param name="statusCounts">The status counts returned by the diagnostics store.</param>
    /// <returns>A read-only map keyed by inbox status name.</returns>
    private static IReadOnlyDictionary<string, object> BuildStatusData(IReadOnlyDictionary<InboxStatus, int> statusCounts)
    {
        var data = new Dictionary<string, object>(statusCounts.Count, StringComparer.Ordinal);

        foreach (var (status, count) in statusCounts)
        {
            data[status.ToString()] = count;
        }

        return data;
    }
}
