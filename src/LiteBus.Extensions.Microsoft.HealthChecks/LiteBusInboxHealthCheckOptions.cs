namespace LiteBus.Extensions.Microsoft.HealthChecks;

/// <summary>
///     Thresholds used by <see cref="InboxHealthCheck" /> when evaluating queue health.
/// </summary>
public sealed class LiteBusInboxHealthCheckOptions
{
    /// <summary>
    ///     Gets or sets the maximum allowed dead-letter count before the health check reports an unhealthy result.
    /// </summary>
    /// <value>
    ///     The inclusive dead-letter ceiling. The default is <see cref="int.MaxValue" />, which stays healthy unless
    ///     the diagnostics query fails.
    /// </value>
    public int MaxDeadLetterCount { get; set; } = int.MaxValue;
}
