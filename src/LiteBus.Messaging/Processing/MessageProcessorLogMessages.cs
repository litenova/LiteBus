using System;
using Microsoft.Extensions.Logging;

namespace LiteBus.Messaging.Processing;

/// <summary>
///     Defines allocation-conscious log messages shared by pipelined message processors.
/// </summary>
internal static class MessageProcessorLogMessages
{
    /// <summary>
    ///     Writes the batch lease debug event.
    /// </summary>
    private static readonly Action<ILogger, string, int, string, Exception?> LeasedBatchMessage =
        LoggerMessage.Define<string, int, string>(
            LogLevel.Debug,
            new EventId(3001, nameof(LeasedBatch)),
            "The {ProcessorName} processor leased {LeasedCount} message(s) as owner {LeaseOwner}.");

    /// <summary>
    ///     Writes the lease renewal failure event.
    /// </summary>
    private static readonly Action<ILogger, string, Guid, string, Exception?> LeaseRenewalFailedMessage =
        LoggerMessage.Define<string, Guid, string>(
            LogLevel.Warning,
            new EventId(3002, nameof(LeaseRenewalFailed)),
            "The {ProcessorName} processor failed to renew the lease for message {MessageId} owned by {LeaseOwner}; canceling dispatch.");

    /// <summary>
    ///     Writes the terminal persistence failure event.
    /// </summary>
    private static readonly Action<ILogger, Guid, Exception?> TerminalPersistenceFailedMessage =
        LoggerMessage.Define<Guid>(
            LogLevel.Error,
            new EventId(3003, nameof(TerminalPersistenceFailed)),
            "Terminal persistence failed for message {MessageId}. Continuing the pass with remaining messages.");

    /// <summary>
    ///     Logs a leased processor batch.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="processorName">The semantic processor name.</param>
    /// <param name="leasedCount">The number of leased messages.</param>
    /// <param name="leaseOwner">The owner assigned to the leases.</param>
    public static void LeasedBatch(ILogger logger, string processorName, int leasedCount, string leaseOwner)
    {
        LeasedBatchMessage(logger, processorName, leasedCount, leaseOwner, null);
    }

    /// <summary>
    ///     Logs a failed lease renewal.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="processorName">The semantic processor name.</param>
    /// <param name="messageId">The leased message identifier.</param>
    /// <param name="leaseOwner">The owner expected to hold the lease.</param>
    public static void LeaseRenewalFailed(
        ILogger logger,
        string processorName,
        Guid messageId,
        string leaseOwner)
    {
        LeaseRenewalFailedMessage(logger, processorName, messageId, leaseOwner, null);
    }

    /// <summary>
    ///     Logs a terminal persistence failure that does not abort the processor pass.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="messageId">The message whose outcome could not be persisted.</param>
    /// <param name="exception">The persistence exception.</param>
    public static void TerminalPersistenceFailed(ILogger logger, Guid messageId, Exception exception)
    {
        TerminalPersistenceFailedMessage(logger, messageId, exception);
    }
}
