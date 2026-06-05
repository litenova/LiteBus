namespace LiteBus.Transport.Aws;

/// <summary>
///     Connection settings for AWS SQS transport adapters.
/// </summary>
public sealed class AwsSqsTransportOptions
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
    ///     Gets the long-poll wait time in seconds used by the consumer.
    /// </summary>
    public int LongPollWaitTimeSeconds { get; init; } = 20;

    /// <summary>
    ///     Gets the default visibility timeout in seconds applied to received messages.
    /// </summary>
    public int VisibilityTimeoutSeconds { get; init; } = 30;
}

