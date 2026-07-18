namespace LiteBus.Transport.AwsSqs;

/// <summary>
///     Connection settings for AWS SQS transport adapters.
/// </summary>
public sealed record AwsSqsTransportOptions
{
    /// <summary>
    ///     Gets the optional AWS region system name such as <c>us-east-1</c>.
    /// </summary>
    public string? Region { get; init; }

    /// <summary>
    ///     Gets the optional service URL used for LocalStack or custom endpoints.
    /// </summary>
    public string? ServiceUrl { get; init; }

    /// <summary>
    ///     Gets the optional access key used when explicit credentials are required.
    /// </summary>
    public string? AccessKey { get; init; }

    /// <summary>
    ///     Gets the optional secret key used when explicit credentials are required.
    /// </summary>
    public string? SecretKey { get; init; }

    /// <summary>
    ///     Gets the queue URL whose attributes are read by the connectivity diagnostic.
    /// </summary>
    /// <value>
    ///     When <see langword="null" />, the diagnostic reports degraded because connectivity cannot be verified with
    ///     least-privilege queue permissions.
    /// </value>
    public string? ConnectivityCheckQueueUrl { get; init; }

    /// <summary>
    ///     Gets the long-poll wait time in seconds used by the consumer.
    /// </summary>
    public int LongPollWaitTimeSeconds { get; init; } = 20;

    /// <summary>
    ///     Gets the default visibility timeout in seconds applied to received messages.
    /// </summary>
    public int VisibilityTimeoutSeconds { get; init; } = 30;

    /// <summary>
    ///     Gets the base visibility timeout in seconds applied when a handler requests requeue.
    /// </summary>
    public int RequeueVisibilityTimeoutSeconds { get; init; } = 30;

    /// <summary>
    ///     Gets the maximum visibility timeout in seconds applied when requeue backoff is computed.
    /// </summary>
    public int MaxRequeueVisibilityTimeoutSeconds { get; init; } = 900;

    /// <summary>
    ///     Gets the multiplier applied to the requeue visibility timeout for each prior receive attempt.
    /// </summary>
    public double RequeueBackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    ///     Gets the initial delay applied before polling again when an entire received batch fails.
    /// </summary>
    public TimeSpan PollBackoffInitial { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    ///     Gets the maximum delay applied before polling again after repeated full-batch failures.
    /// </summary>
    public TimeSpan PollBackoffMax { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Gets the multiplier applied to the poll backoff delay after each consecutive full-batch failure.
    /// </summary>
    public double PollBackoffMultiplier { get; init; } = 2.0;
}
