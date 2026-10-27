using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     High-performance PostgreSQL recurring lease log messages.
/// </summary>
internal static class PostgreSqlRecurringLeaseLogMessages
{
    /// <summary>
    ///     The delegate that writes a stolen-lease renewal event.
    /// </summary>
    private static readonly Action<ILogger, string, string, Exception?> LeaseRenewalStolenMessage =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2110, nameof(LeaseRenewalStolen)),
            "The recurring lease '{LeaseName}' is no longer held by '{Holder}'; another holder took it. Renewal stopped and the lease row is left alone.");

    /// <summary>
    ///     The delegate that writes a failed-renewal event.
    /// </summary>
    private static readonly Action<ILogger, string, Exception> LeaseRenewalFailedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2111, nameof(LeaseRenewalFailed)),
            "Renewing the recurring lease '{LeaseName}' failed. Renewal stopped; the lease expires on the database clock.");

    /// <summary>
    ///     Writes a stolen-lease renewal event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="leaseName">The lease name another holder took.</param>
    /// <param name="holder">The holder identity whose renewal found the lease taken.</param>
    internal static void LeaseRenewalStolen(ILogger logger, string leaseName, string holder)
    {
        LeaseRenewalStolenMessage(logger, leaseName, holder, null);
    }

    /// <summary>
    ///     Writes a failed-renewal event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="leaseName">The lease name whose renewal failed.</param>
    /// <param name="exception">The exception the renewal threw.</param>
    internal static void LeaseRenewalFailed(ILogger logger, string leaseName, Exception exception)
    {
        LeaseRenewalFailedMessage(logger, leaseName, exception);
    }
}
