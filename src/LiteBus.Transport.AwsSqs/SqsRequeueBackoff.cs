using System.Globalization;
using Amazon.SQS.Model;

namespace LiteBus.Transport.AwsSqs;

/// <summary>
///     Computes SQS visibility timeouts and poll backoff delays from receive counts and transport options.
/// </summary>
internal static class SqsRequeueBackoff
{
    /// <summary>
    ///     Computes the visibility timeout to apply when a handler requests requeue.
    /// </summary>
    /// <param name="message">The received SQS message.</param>
    /// <param name="options">The transport options supplying backoff tuning values.</param>
    /// <returns>The visibility timeout in seconds.</returns>
    public static int ComputeRequeueVisibilityTimeout(Message message, AwsSqsTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(options);

        var receiveCount = GetApproximateReceiveCount(message);
        var multiplier = Math.Pow(options.RequeueBackoffMultiplier, receiveCount - 1);
        var timeout = options.RequeueVisibilityTimeoutSeconds * multiplier;
        var capped = Math.Min(timeout, options.MaxRequeueVisibilityTimeoutSeconds);
        return Math.Max(1, (int) Math.Ceiling(capped));
    }

    /// <summary>
    ///     Computes the poll delay to apply after consecutive full-batch failures.
    /// </summary>
    /// <param name="consecutiveBatchFailures">The number of consecutive batches where every handler failed.</param>
    /// <param name="options">The transport options supplying backoff tuning values.</param>
    /// <returns>The capped poll backoff delay.</returns>
    public static TimeSpan ComputePollBackoff(int consecutiveBatchFailures, AwsSqsTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (consecutiveBatchFailures <= 0)
        {
            return TimeSpan.Zero;
        }

        var multiplier = Math.Pow(options.PollBackoffMultiplier, consecutiveBatchFailures - 1);
        var delay = TimeSpan.FromMilliseconds(options.PollBackoffInitial.TotalMilliseconds * multiplier);
        return delay <= options.PollBackoffMax ? delay : options.PollBackoffMax;
    }

    /// <summary>
    ///     Reads the approximate receive count attribute when present on the SQS message.
    /// </summary>
    /// <param name="message">The received SQS message.</param>
    /// <returns>The receive count, defaulting to one when the attribute is absent.</returns>
    private static int GetApproximateReceiveCount(Message message)
    {
        if (!message.Attributes.TryGetValue("ApproximateReceiveCount", out var count) ||
            !int.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out var receiveCount) ||
            receiveCount < 1)
        {
            return 1;
        }

        return receiveCount;
    }
}