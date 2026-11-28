using System;

namespace LiteBus.Auditing.Anchors.AzureBlob;

/// <summary>
///     Thrown when the Azure Blob checkpoint anchor cannot publish or read back a checkpoint.
/// </summary>
/// <remarks>
///     A republish of the same checkpoint succeeds; a different checkpoint under an already stored checkpoint
///     identifier, an unreadable anchor blob, and an unknown stored format all throw this instead of passing silently.
///     Storage I/O failures surface as the Azure SDK exception, so callers can read the failure status from it.
/// </remarks>
public sealed class AzureBlobCheckpointAnchorException : InvalidOperationException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureBlobCheckpointAnchorException" /> class.
    /// </summary>
    /// <param name="message">The message describing which checkpoint or blob failed.</param>
    public AzureBlobCheckpointAnchorException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureBlobCheckpointAnchorException" /> class.
    /// </summary>
    /// <param name="message">The message describing which checkpoint or blob failed.</param>
    /// <param name="innerException">The exception that reported the failure.</param>
    public AzureBlobCheckpointAnchorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
