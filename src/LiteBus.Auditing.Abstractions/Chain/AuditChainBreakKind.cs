using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     What is wrong at a position of an audit chain.
/// </summary>
public enum AuditChainBreakKind
{
    /// <summary>
    ///     A sequence number is missing.
    /// </summary>
    Gap = 0,

    /// <summary>
    ///     A sequence number appears more than once, or out of order.
    /// </summary>
    Duplicate = 1,

    /// <summary>
    ///     The stored previous hash does not match the hash of the document before it.
    /// </summary>
    PreviousHashMismatch = 2,

    /// <summary>
    ///     The stored hash does not match the hash recomputed from the document.
    /// </summary>
    HashMismatch = 3,

    /// <summary>
    ///     The document names a hash scheme the chain does not accept.
    /// </summary>
    UnknownHashScheme = 4,

    /// <summary>
    ///     The hash at the latest checkpoint's sequence differs from the checkpoint's hash.
    /// </summary>
    CheckpointHashMismatch = 5,

    /// <summary>
    ///     The segment holds fewer documents up to the checkpoint than the checkpoint counted.
    /// </summary>
    CheckpointCountMismatch = 6,

    /// <summary>
    ///     The latest checkpoint's signature is missing or invalid.
    /// </summary>
    CheckpointSignatureInvalid = 7,

    /// <summary>
    ///     No checkpoint exists where one is required: the segment holds no checkpoint although the chain's checkpoint
    ///     policy would already have cut one, or the checkpoint a verification was asked to trust does not exist.
    /// </summary>
    CheckpointMissing = 8,

    /// <summary>
    ///     The last checkpoint is older than the configured age while records exist after it.
    /// </summary>
    CheckpointStale = 9,

    /// <summary>
    ///     A stored checkpoint still waits for publication.
    /// </summary>
    CheckpointUnpublished = 10,

    /// <summary>
    ///     A record was staged but remains unlinked past the configured age.
    /// </summary>
    Unlinked = 11
}
