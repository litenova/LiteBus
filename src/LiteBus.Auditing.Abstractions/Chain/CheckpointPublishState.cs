using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Whether a stored audit checkpoint reached its publishers.
/// </summary>
/// <remarks>
///     A checkpoint is cut as <see cref="Pending" /> and published by a background retrier. A failed attempt stays
///     pending with the attempt count and the last error recorded on the checkpoint; publication never runs inside an
///     append.
/// </remarks>
public enum CheckpointPublishState
{
    /// <summary>
    ///     The checkpoint is stored and waits for publication.
    /// </summary>
    Pending = 0,

    /// <summary>
    ///     The checkpoint reached every configured publisher.
    /// </summary>
    Published = 1
}
