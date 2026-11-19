using System;

namespace LiteBus.Auditing.Anchors.S3;

/// <summary>
///     Thrown when the S3 checkpoint anchor cannot publish or read back a checkpoint.
/// </summary>
/// <remarks>
///     A checkpoint that cannot be rendered as checkpoint text and an unreadable anchor object throw this instead of
///     passing silently. Storage I/O failures surface as the AWS SDK exception, so callers can read the failure status
///     from it.
/// </remarks>
public sealed class S3CheckpointAnchorException : InvalidOperationException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="S3CheckpointAnchorException" /> class.
    /// </summary>
    /// <param name="message">The message describing which checkpoint or object failed.</param>
    public S3CheckpointAnchorException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="S3CheckpointAnchorException" /> class.
    /// </summary>
    /// <param name="message">The message describing which checkpoint or object failed.</param>
    /// <param name="innerException">The exception that reported the failure.</param>
    public S3CheckpointAnchorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
