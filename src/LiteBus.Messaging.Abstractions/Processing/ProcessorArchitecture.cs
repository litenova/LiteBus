namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Selects the inbox or outbox processor execution model.
/// </summary>
public enum ProcessorArchitecture
{
    /// <summary>
    ///     Uses the channel-based pipeline with parallel dispatch workers and lease heartbeat renewal.
    /// </summary>
    Pipelined = 0,

    /// <summary>
    ///     Uses the original sequential foreach loop that dispatches one leased envelope at a time.
    /// </summary>
    Legacy = 1
}
