namespace LiteBus.Transport.IntegrationTesting;

/// <summary>
///     xUnit category names used to filter durable transport integration tests in CI.
/// </summary>
public static class TransportTestTraits
{
    /// <summary>
    ///     Fast transport tests that run without Docker (InMemory and registration).
    /// </summary>
    public const string Fast = "TransportFast";

    /// <summary>
    ///     Transport tests that require Docker (Kafka, LocalStack SQS).
    /// </summary>
    public const string Docker = "TransportDocker";

    /// <summary>
    ///     Transport tests that require the Azure Service Bus emulator.
    /// </summary>
    public const string Azure = "TransportAzure";

    /// <summary>
    ///     Optional transport tests that require explicit live Azure Service Bus credentials.
    /// </summary>
    public const string LiveAzure = "TransportLiveAzure";

    /// <summary>
    ///     AMQP transport tests documented for matrix completeness; AMQP wire tests live in sibling projects.
    /// </summary>
    public const string Amqp = "TransportAmqp";
}
