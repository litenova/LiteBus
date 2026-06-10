using System;
using Confluent.Kafka;

namespace LiteBus.Transport.Kafka;

/// <summary>
///     Computes seek backoff delays and tracks repeated failures at the same topic-partition offset.
/// </summary>
internal sealed class KafkaSeekBackoff
{
    /// <summary>
    ///     Gets the transport options supplying backoff tuning values.
    /// </summary>
    private readonly KafkaTransportOptions _options;

    /// <summary>
    ///     Gets the offset that most recently failed and triggered a seek.
    /// </summary>
    private TopicPartitionOffset? _lastFailedOffset;

    /// <summary>
    ///     Gets the number of consecutive seek failures observed at <see cref="_lastFailedOffset" />.
    /// </summary>
    private int _consecutiveFailures;

    /// <summary>
    ///     Initializes a new instance of the <see cref="KafkaSeekBackoff" /> class.
    /// </summary>
    /// <param name="options">The transport options supplying backoff tuning values.</param>
    public KafkaSeekBackoff(KafkaTransportOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    ///     Records a seek for the supplied offset and returns the delay to apply before the next consume call.
    /// </summary>
    /// <param name="offset">The offset that failed processing and was rewound.</param>
    /// <returns>The backoff delay to apply before consuming again.</returns>
    public TimeSpan RecordSeek(TopicPartitionOffset offset)
    {
        ArgumentNullException.ThrowIfNull(offset);

        if (_lastFailedOffset == offset)
        {
            _consecutiveFailures++;
        }
        else
        {
            _lastFailedOffset = offset;
            _consecutiveFailures = 1;
        }

        return ComputeDelay(_consecutiveFailures);
    }

    /// <summary>
    ///     Clears failure tracking after the offset is committed successfully.
    /// </summary>
    /// <param name="offset">The offset that was committed.</param>
    public void RecordCommit(TopicPartitionOffset offset)
    {
        ArgumentNullException.ThrowIfNull(offset);

        if (_lastFailedOffset == offset)
        {
            _lastFailedOffset = null;
            _consecutiveFailures = 0;
        }
    }

    /// <summary>
    ///     Computes the exponential backoff delay for the supplied failure count.
    /// </summary>
    /// <param name="failureCount">The number of consecutive failures at the same offset.</param>
    /// <returns>The capped backoff delay.</returns>
    private TimeSpan ComputeDelay(int failureCount)
    {
        var multiplier = Math.Pow(_options.SeekFailureBackoffMultiplier, failureCount - 1);
        var delay = TimeSpan.FromMilliseconds(_options.SeekFailureBackoffInitial.TotalMilliseconds * multiplier);
        return delay <= _options.SeekFailureBackoffMax ? delay : _options.SeekFailureBackoffMax;
    }
}
