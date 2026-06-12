namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Records per-pass envelope outcomes from processor dispatch workers.
/// </summary>
/// <typeparam name="TEnvelope">The envelope type collected for persistence.</typeparam>
public interface IProcessorPassRecorder<in TEnvelope>
{
    /// <summary>
    ///     Records one successful envelope outcome.
    /// </summary>
    /// <param name="envelope">The post-transition envelope to persist.</param>
    void RecordSucceeded(TEnvelope envelope);

    /// <summary>
    ///     Records one retryable failure outcome.
    /// </summary>
    /// <param name="envelope">The post-transition envelope to persist.</param>
    void RecordFailed(TEnvelope envelope);

    /// <summary>
    ///     Records one dead-letter outcome.
    /// </summary>
    /// <param name="envelope">The post-transition envelope to persist.</param>
    void RecordDeadLettered(TEnvelope envelope);
}