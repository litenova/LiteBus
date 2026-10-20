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
    ///     Writes the lease release debug event.
    /// </summary>
    private static readonly Action<ILogger, string, Guid, bool, Exception?> LeaseReleasedMessage =
        LoggerMessage.Define<string, Guid, bool>(
            LogLevel.Debug,
            new EventId(3011, nameof(LeaseReleased)),
            "The {ProcessorName} processor released the lease for cancelled message {MessageId} (released: {Released}).");

    /// <summary>
    ///     Writes the lease release failure event.
    /// </summary>
    private static readonly Action<ILogger, string, Guid, Exception?> LeaseReleaseFailedMessage =
        LoggerMessage.Define<string, Guid>(
            LogLevel.Warning,
            new EventId(3012, nameof(LeaseReleaseFailed)),
            "The {ProcessorName} processor could not release the lease for cancelled message {MessageId}. The lease expires and the message is reclaimed with the attempt counted.");

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
    /// <param name="exception">The renewal exception, when the store call threw.</param>
    public static void LeaseRenewalFailed(
        ILogger logger,
        string processorName,
        Guid messageId,
        string leaseOwner,
        Exception? exception = null)
    {
        LeaseRenewalFailedMessage(logger, processorName, messageId, leaseOwner, exception);
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

    /// <summary>
    ///     Logs the release of a lease whose dispatch was cancelled.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="processorName">The semantic processor name.</param>
    /// <param name="messageId">The released message identifier.</param>
    /// <param name="released">Whether the store released the row; <see langword="false" /> when the lease had moved on.</param>
    public static void LeaseReleased(ILogger logger, string processorName, Guid messageId, bool released)
    {
        LeaseReleasedMessage(logger, processorName, messageId, released, null);
    }

    /// <summary>
    ///     Logs a failed lease release.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="processorName">The semantic processor name.</param>
    /// <param name="messageId">The message whose lease could not be released.</param>
    /// <param name="exception">The release exception.</param>
    public static void LeaseReleaseFailed(ILogger logger, string processorName, Guid messageId, Exception exception)
    {
        LeaseReleaseFailedMessage(logger, processorName, messageId, exception);
    }
}
