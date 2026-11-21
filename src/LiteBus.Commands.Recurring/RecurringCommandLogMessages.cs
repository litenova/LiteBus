using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     High-performance recurring command loop log messages.
/// </summary>
internal static class RecurringCommandLogMessages
{
    /// <summary>
    ///     The delegate that writes the loop start event.
    /// </summary>
    private static readonly Action<ILogger, string, TimeSpan, Exception?> LoopStartedMessage =
        LoggerMessage.Define<string, TimeSpan>(
            LogLevel.Information,
            new EventId(2101, nameof(LoopStarted)),
            "Recurring command loop for {CommandType} started with interval {Interval}.");

    /// <summary>
    ///     The delegate that writes the disabled-loop event.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> LoopDisabledMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2102, nameof(LoopDisabled)),
            "Recurring command loop for {CommandType} is disabled and will not run.");

    /// <summary>
    ///     The delegate that writes a failed-pass event.
    /// </summary>
    private static readonly Action<ILogger, string, Exception> PassFailedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2103, nameof(PassFailed)),
            "Recurring command {CommandType} failed. The loop continues after the configured error backoff.");

    /// <summary>
    ///     The delegate that writes a concurrency-conflict event.
    /// </summary>
    private static readonly Action<ILogger, string, string, Exception?> PassConflictedMessage =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(2104, nameof(PassConflicted)),
            "Recurring command {CommandType} lost a concurrency race ({ConflictType}); another instance changed the same data. The loop continues at its normal interval.");

    /// <summary>
    ///     The delegate that writes a lease-skipped event.
    /// </summary>
    private static readonly Action<ILogger, string, string, Exception?> LeaseSkippedMessage =
        LoggerMessage.Define<string, string>(
            LogLevel.Debug,
            new EventId(2105, nameof(LeaseSkipped)),
            "Recurring command {CommandType} skipped a pass because the '{LeaseName}' lease is held by another instance. The loop continues at its normal interval.");

    /// <summary>
    ///     The delegate that writes a lease-lost event.
    /// </summary>
    private static readonly Action<ILogger, string, string, Exception?> LeaseLostMessage =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2106, nameof(LeaseLost)),
            "Recurring command {CommandType} lost the '{LeaseName}' lease mid-pass: renewal stopped, so another instance may take over. The busy period ends and the loop backs off.");

    /// <summary>
    ///     The delegate that writes a batch-refused event.
    /// </summary>
    private static readonly Action<ILogger, string, int, string, string, Exception?> BatchRefusedMessage =
        LoggerMessage.Define<string, int, string, string>(
            LogLevel.Warning,
            new EventId(2107, nameof(BatchRefused)),
            "Recurring batch source {SourceType} rolled back: the command at index {FailedIndex} ({CommandType}) was refused ({RefusalCode}). The loop continues after the configured error backoff.");

    /// <summary>
    ///     The delegate that writes a batch-item-refused event.
    /// </summary>
    private static readonly Action<ILogger, string, int, string, string, Exception?> BatchItemRefusedMessage =
        LoggerMessage.Define<string, int, string, string>(
            LogLevel.Warning,
            new EventId(2113, nameof(BatchItemRefused)),
            "Recurring batch source {SourceType}: the command at index {FailedIndex} ({CommandType}) was refused ({RefusalCode}). The batch continues with the next item.");

    /// <summary>
    ///     The delegate that writes a batch-item-failed event.
    /// </summary>
    private static readonly Action<ILogger, string, int, string, Exception> BatchItemFailedMessage =
        LoggerMessage.Define<string, int, string>(
            LogLevel.Error,
            new EventId(2108, nameof(BatchItemFailed)),
            "Recurring batch source {SourceType}: the command at index {FailedIndex} ({CommandType}) failed. The batch continues with the next item.");

    /// <summary>
    ///     The delegate that writes a batch-finished-with-failures event.
    /// </summary>
    private static readonly Action<ILogger, string, int, int, Exception?> BatchCompletedWithFailuresMessage =
        LoggerMessage.Define<string, int, int>(
            LogLevel.Warning,
            new EventId(2109, nameof(BatchCompletedWithFailures)),
            "Recurring batch source {SourceType} finished with {FailedCount} of {TotalCount} items failed. The loop continues after the configured error backoff.");

    /// <summary>
    ///     The delegate that writes a tenant-command-failed event.
    /// </summary>
    private static readonly Action<ILogger, string, string, Exception> TenantCommandFailedMessage =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(2110, nameof(TenantCommandFailed)),
            "Recurring tenant sweep {CommandType} failed for tenant {TenantId}. The sweep continues with the next tenant.");

    /// <summary>
    ///     The delegate that writes a tenant-command-refused event.
    /// </summary>
    private static readonly Action<ILogger, string, string, string, Exception?> TenantCommandRefusedMessage =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Warning,
            new EventId(2111, nameof(TenantCommandRefused)),
            "Recurring tenant sweep {CommandType} command for tenant {TenantId} was refused ({RefusalCode}). The sweep continues with the next tenant.");

    /// <summary>
    ///     The delegate that writes a sweep-finished-with-failures event.
    /// </summary>
    private static readonly Action<ILogger, string, int, int, Exception?> SweepCompletedWithFailuresMessage =
        LoggerMessage.Define<string, int, int>(
            LogLevel.Warning,
            new EventId(2112, nameof(SweepCompletedWithFailures)),
            "Recurring tenant sweep {CommandType} finished with {FailedCount} of {TotalCount} tenants failed. The loop continues after the configured error backoff.");

    /// <summary>
    ///     The delegate that writes a missed-occurrences event.
    /// </summary>
    private static readonly Action<ILogger, string, int, Exception?> MissedOccurrencesMessage =
        LoggerMessage.Define<string, int>(
            LogLevel.Warning,
            new EventId(2114, nameof(MissedOccurrences)),
            "Recurring command {CommandType} missed {MissedCount} occurrence(s) while down or busy.");

    /// <summary>
    ///     The delegate that writes a truncated missed-occurrences event.
    /// </summary>
    private static readonly Action<ILogger, string, int, Exception?> TruncatedMissedOccurrencesMessage =
        LoggerMessage.Define<string, int>(
            LogLevel.Warning,
            new EventId(2115, nameof(TruncatedMissedOccurrences)),
            "Recurring command {CommandType} missed more than {MissedCount} counted occurrence(s) while down or busy; the loop skips to the next occurrence after now.");

    /// <summary>
    ///     The delegate that writes a catch-up-pass event.
    /// </summary>
    private static readonly Action<ILogger, string, DateTimeOffset, Exception?> CatchUpPassFiredMessage =
        LoggerMessage.Define<string, DateTimeOffset>(
            LogLevel.Information,
            new EventId(2116, nameof(CatchUpPassFired)),
            "Recurring command {CommandType} runs one catch-up pass for the missed occurrence at {Occurrence}.");

    /// <summary>
    ///     The delegate that writes a sweep missed-occurrences event.
    /// </summary>
    private static readonly Action<ILogger, string, int, int, Exception?> SweepMissedOccurrencesMessage =
        LoggerMessage.Define<string, int, int>(
            LogLevel.Warning,
            new EventId(2117, nameof(SweepMissedOccurrences)),
            "Recurring tenant sweep {CommandType} missed {MissedCount} occurrence(s) across {TenantCount} tenant(s) while down or busy.");

    /// <summary>
    ///     The delegate that writes a sweep catch-up-passes event.
    /// </summary>
    private static readonly Action<ILogger, string, int, Exception?> SweepCatchUpPassesFiredMessage =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(2118, nameof(SweepCatchUpPassesFired)),
            "Recurring tenant sweep {CommandType} runs catch-up passes for {TenantCount} tenant(s) with a missed occurrence inside the catch-up window.");

    /// <summary>
    ///     The delegate that writes a tenant-lease-skipped event.
    /// </summary>
    private static readonly Action<ILogger, string, string, string, Exception?> TenantLeaseSkippedMessage =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Debug,
            new EventId(2119, nameof(TenantLeaseSkipped)),
            "Recurring tenant sweep {CommandType} skipped tenant {TenantId} because the '{LeaseName}' lease is held by another instance. The sweep continues with the next tenant.");

    /// <summary>
    ///     The delegate that writes a sweep-tenants-not-due event.
    /// </summary>
    private static readonly Action<ILogger, string, int, int, Exception?> SweepTenantsNotDueMessage =
        LoggerMessage.Define<string, int, int>(
            LogLevel.Debug,
            new EventId(2120, nameof(SweepTenantsNotDue)),
            "Recurring tenant sweep {CommandType} skipped {SkippedCount} of {TotalCount} tenant(s) with no due occurrence.");

    /// <summary>
    ///     The delegate that writes an occurrence-store-fallback event.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> OccurrenceStoreFallbackMessage =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(2122, nameof(OccurrenceStoreFallback)),
            "Recurring command {CommandType} has no IRecurringOccurrenceStore registered; occurrences are tracked in process memory only and a miss across a restart is best-effort.");

    /// <summary>
    ///     Writes the loop start event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type sent by the loop.</param>
    /// <param name="interval">The delay between passes.</param>
    internal static void LoopStarted(ILogger logger, string commandType, TimeSpan interval)
    {
        LoopStartedMessage(logger, commandType, interval, null);
    }

    /// <summary>
    ///     Writes the disabled-loop event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type the disabled loop would have sent.</param>
    internal static void LoopDisabled(ILogger logger, string commandType)
    {
        LoopDisabledMessage(logger, commandType, null);
    }

    /// <summary>
    ///     Writes a failed-pass event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type sent by the loop.</param>
    /// <param name="exception">The exception the pass threw.</param>
    internal static void PassFailed(ILogger logger, string commandType, Exception exception)
    {
        PassFailedMessage(logger, commandType, exception);
    }

    /// <summary>
    ///     Writes a concurrency-conflict event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type sent by the loop.</param>
    /// <param name="conflictType">The type name of the exception classified as a conflict.</param>
    internal static void PassConflicted(ILogger logger, string commandType, string conflictType)
    {
        PassConflictedMessage(logger, commandType, conflictType, null);
    }

    /// <summary>
    ///     Writes a lease-skipped event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type sent by the loop.</param>
    /// <param name="leaseName">The lease name another instance holds.</param>
    internal static void LeaseSkipped(ILogger logger, string commandType, string leaseName)
    {
        LeaseSkippedMessage(logger, commandType, leaseName, null);
    }

    /// <summary>
    ///     Writes a lease-lost event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type sent by the loop.</param>
    /// <param name="leaseName">The lease name that was lost.</param>
    internal static void LeaseLost(ILogger logger, string commandType, string leaseName)
    {
        LeaseLostMessage(logger, commandType, leaseName, null);
    }

    /// <summary>
    ///     Writes a batch-refused event for an atomic batch a refusal rolled back.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="sourceType">The batch source type whose batch was refused.</param>
    /// <param name="failedIndex">The index of the command that stopped the batch.</param>
    /// <param name="commandType">The command type that was refused.</param>
    /// <param name="refusalCode">The machine-readable code the refusal supplied.</param>
    internal static void BatchRefused(ILogger logger, string sourceType, int failedIndex, string commandType, string refusalCode)
    {
        BatchRefusedMessage(logger, sourceType, failedIndex, commandType, refusalCode, null);
    }

    /// <summary>
    ///     Writes a batch-item-refused event for one refused item of a per-item batch.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="sourceType">The batch source type whose item was refused.</param>
    /// <param name="failedIndex">The index of the command that was refused.</param>
    /// <param name="commandType">The command type that was refused.</param>
    /// <param name="refusalCode">The machine-readable code the refusal supplied.</param>
    internal static void BatchItemRefused(ILogger logger, string sourceType, int failedIndex, string commandType, string refusalCode)
    {
        BatchItemRefusedMessage(logger, sourceType, failedIndex, commandType, refusalCode, null);
    }

    /// <summary>
    ///     Writes a batch-item-failed event for one failed item of a per-item batch.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="sourceType">The batch source type whose item failed.</param>
    /// <param name="failedIndex">The index of the command that failed.</param>
    /// <param name="commandType">The command type that failed.</param>
    /// <param name="exception">The exception the item threw.</param>
    internal static void BatchItemFailed(ILogger logger, string sourceType, int failedIndex, string commandType, Exception exception)
    {
        BatchItemFailedMessage(logger, sourceType, failedIndex, commandType, exception);
    }

    /// <summary>
    ///     Writes a batch-finished-with-failures event for a per-item batch that finished with failures.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="sourceType">The batch source type whose batch finished with failures.</param>
    /// <param name="failedCount">The number of items that failed or were refused.</param>
    /// <param name="totalCount">The number of items the batch held.</param>
    internal static void BatchCompletedWithFailures(ILogger logger, string sourceType, int failedCount, int totalCount)
    {
        BatchCompletedWithFailuresMessage(logger, sourceType, failedCount, totalCount, null);
    }

    /// <summary>
    ///     Writes a tenant-command-failed event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type the sweep sends.</param>
    /// <param name="tenantId">The tenant whose command failed.</param>
    /// <param name="exception">The exception the tenant's send threw.</param>
    internal static void TenantCommandFailed(ILogger logger, string commandType, string tenantId, Exception exception)
    {
        TenantCommandFailedMessage(logger, commandType, tenantId, exception);
    }

    /// <summary>
    ///     Writes a tenant-command-refused event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type the sweep sends.</param>
    /// <param name="tenantId">The tenant whose command was refused.</param>
    /// <param name="refusalCode">The machine-readable code the refusal supplied.</param>
    internal static void TenantCommandRefused(ILogger logger, string commandType, string tenantId, string refusalCode)
    {
        TenantCommandRefusedMessage(logger, commandType, tenantId, refusalCode, null);
    }

    /// <summary>
    ///     Writes a sweep-finished-with-failures event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type the sweep sends.</param>
    /// <param name="failedCount">The number of tenants whose command failed or was refused.</param>
    /// <param name="totalCount">The number of tenants the sweep sent for.</param>
    internal static void SweepCompletedWithFailures(ILogger logger, string commandType, int failedCount, int totalCount)
    {
        SweepCompletedWithFailuresMessage(logger, commandType, failedCount, totalCount, null);
    }

    /// <summary>
    ///     Writes a missed-occurrences event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type sent by the loop.</param>
    /// <param name="missedCount">The number of occurrences that passed without running.</param>
    internal static void MissedOccurrences(ILogger logger, string commandType, int missedCount)
    {
        MissedOccurrencesMessage(logger, commandType, missedCount, null);
    }

    /// <summary>
    ///     Writes a truncated missed-occurrences event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type sent by the loop.</param>
    /// <param name="missedCount">The number of occurrences counted before evaluation stopped.</param>
    internal static void TruncatedMissedOccurrences(ILogger logger, string commandType, int missedCount)
    {
        TruncatedMissedOccurrencesMessage(logger, commandType, missedCount, null);
    }

    /// <summary>
    ///     Writes a catch-up-pass event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type sent by the loop.</param>
    /// <param name="occurrence">The missed occurrence the catch-up pass runs for.</param>
    internal static void CatchUpPassFired(ILogger logger, string commandType, DateTimeOffset occurrence)
    {
        CatchUpPassFiredMessage(logger, commandType, occurrence, null);
    }

    /// <summary>
    ///     Writes a sweep missed-occurrences event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type the sweep sends.</param>
    /// <param name="missedCount">The number of occurrences that passed without running, counted per tenant.</param>
    /// <param name="tenantCount">The number of tenants with a missed occurrence.</param>
    /// <remarks>
    ///     Counts are capped per tenant where evaluation stopped early; see
    ///     <see cref="RecurringOccurrencePlanner" />.
    /// </remarks>
    internal static void SweepMissedOccurrences(ILogger logger, string commandType, int missedCount, int tenantCount)
    {
        SweepMissedOccurrencesMessage(logger, commandType, missedCount, tenantCount, null);
    }

    /// <summary>
    ///     Writes a sweep catch-up-passes event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type the sweep sends.</param>
    /// <param name="tenantCount">The number of tenants with a catch-up pass.</param>
    internal static void SweepCatchUpPassesFired(ILogger logger, string commandType, int tenantCount)
    {
        SweepCatchUpPassesFiredMessage(logger, commandType, tenantCount, null);
    }

    /// <summary>
    ///     Writes a tenant-lease-skipped event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type the sweep sends.</param>
    /// <param name="tenantId">The tenant whose lease another instance holds.</param>
    /// <param name="leaseName">The lease name another instance holds.</param>
    internal static void TenantLeaseSkipped(ILogger logger, string commandType, string tenantId, string leaseName)
    {
        TenantLeaseSkippedMessage(logger, commandType, tenantId, leaseName, null);
    }

    /// <summary>
    ///     Writes a sweep-tenants-not-due event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type the sweep sends.</param>
    /// <param name="skippedCount">The number of tenants with no due occurrence.</param>
    /// <param name="totalCount">The number of tenants the sweep enumerated.</param>
    internal static void SweepTenantsNotDue(ILogger logger, string commandType, int skippedCount, int totalCount)
    {
        SweepTenantsNotDueMessage(logger, commandType, skippedCount, totalCount, null);
    }

    /// <summary>
    ///     Writes an occurrence-store-fallback event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="commandType">The command type sent by the loop.</param>
    internal static void OccurrenceStoreFallback(ILogger logger, string commandType)
    {
        OccurrenceStoreFallbackMessage(logger, commandType, null);
    }
}
