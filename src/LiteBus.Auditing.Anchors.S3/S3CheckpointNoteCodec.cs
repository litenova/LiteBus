using System;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Checkpoints;

namespace LiteBus.Auditing.Anchors.S3;

/// <summary>
///     Renders checkpoints as transparency-log checkpoint text and reads them back.
/// </summary>
/// <remarks>
///     The text follows <c>c2sp.org/tlog-checkpoint</c> through <see cref="TlogCheckpointCodec" />: the origin line
///     carries the segment, the tree size carries the covered document count, the root hash carries the checkpoint
///     hash, and LiteBus extension lines carry the sequence, scheme, kind, label, creation time, and identifier, so
///     witnesses, timestamping authorities, and generic verifiers consume anchored checkpoints without LiteBus code
///     while this anchor reads back the full checkpoint.
/// </remarks>
internal static class S3CheckpointNoteCodec
{
    /// <summary>
    ///     Renders a checkpoint as the transparency-log checkpoint text stored in its anchor object.
    /// </summary>
    /// <param name="objectKey">The anchor object the text is written to, named when the checkpoint cannot be rendered.</param>
    /// <param name="checkpoint">The checkpoint to render.</param>
    /// <returns>The anchor object text.</returns>
    /// <exception cref="S3CheckpointAnchorException">
    ///     Thrown when the checkpoint carries a value the text shape cannot hold, such as a line break or a
    ///     signature that is not canonical base64.
    /// </exception>
    public static string Serialize(string objectKey, AuditCheckpoint checkpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(checkpoint);

        try
        {
            return TlogCheckpointCodec.Encode(checkpoint);
        }
        catch (ArgumentException exception)
        {
            throw new S3CheckpointAnchorException(
                $"The checkpoint '{checkpoint.Id:D}' for segment '{checkpoint.Segment}' cannot be written to " +
                $"object '{objectKey}': {exception.Message}",
                exception);
        }
    }

    /// <summary>
    ///     Reads anchor object text back as a checkpoint.
    /// </summary>
    /// <param name="objectKey">The anchor object the text was read from, named when the text is invalid.</param>
    /// <param name="content">The anchor object text.</param>
    /// <returns>The checkpoint the object holds.</returns>
    /// <exception cref="S3CheckpointAnchorException">
    ///     Thrown when the text is not a transparency-log checkpoint.
    /// </exception>
    public static AuditCheckpoint Deserialize(string objectKey, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(content);

        if (!TlogCheckpointCodec.TryParse(content, out var checkpoint) || checkpoint is null)
        {
            throw new S3CheckpointAnchorException(
                $"The checkpoint object '{objectKey}' does not hold a checkpoint.");
        }

        return checkpoint;
    }
}
