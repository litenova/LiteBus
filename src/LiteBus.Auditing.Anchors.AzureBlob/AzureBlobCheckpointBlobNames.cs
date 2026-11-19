using System;

namespace LiteBus.Auditing.Anchors.AzureBlob;

/// <summary>
///     Builds the blob names checkpoint blobs are stored under and the prefixes one segment is listed with.
/// </summary>
internal static class AzureBlobCheckpointBlobNames
{
    /// <summary>
    ///     The file suffix marking a blob as a stored checkpoint.
    /// </summary>
    private const string CheckpointSuffix = ".json";

    /// <summary>
    ///     Builds the blob name one checkpoint is stored under.
    /// </summary>
    /// <param name="blobNamePrefix">The configured blob name prefix.</param>
    /// <param name="segment">The checkpoint segment.</param>
    /// <param name="sequence">The sequence of the last document the checkpoint covers.</param>
    /// <param name="checkpointId">The checkpoint identifier, which keeps the name write-once per checkpoint.</param>
    /// <returns>The blob name holding the checkpoint.</returns>
    public static string GetBlobName(string blobNamePrefix, string segment, long sequence, Guid checkpointId)
    {
        return $"{GetSegmentPrefix(blobNamePrefix, segment)}{sequence:D20}-{checkpointId:N}{CheckpointSuffix}";
    }

    /// <summary>
    ///     Builds the blob name prefix listing every checkpoint of one segment and nothing else.
    /// </summary>
    /// <param name="blobNamePrefix">The configured blob name prefix.</param>
    /// <param name="segment">The segment to list.</param>
    /// <returns>The prefix matching exactly the blobs of <paramref name="segment" />.</returns>
    /// <remarks>
    ///     The segment is escaped, so a slash inside it cannot turn into a virtual directory: without escaping, the
    ///     prefix of segment <c>tenant</c> would also match the blobs of segment <c>tenant/2026</c>.
    /// </remarks>
    public static string GetSegmentPrefix(string blobNamePrefix, string segment)
    {
        return $"{NormalizePrefix(blobNamePrefix)}{Uri.EscapeDataString(segment)}/";
    }

    /// <summary>
    ///     Determines whether a blob name marks a stored checkpoint.
    /// </summary>
    /// <param name="blobName">The blob name to check.</param>
    /// <returns><see langword="true" /> when the blob holds a checkpoint; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     Blobs without the checkpoint suffix do not belong to this anchor and are ignored when a segment is read
    ///     back, so foreign files under a shared prefix never break a read.
    /// </remarks>
    public static bool IsCheckpointBlob(string blobName)
    {
        return blobName.EndsWith(CheckpointSuffix, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Normalizes a configured blob name prefix to a form blob names can be appended to.
    /// </summary>
    /// <param name="prefix">The configured prefix.</param>
    /// <returns>The prefix with a trailing slash, or an empty string when no prefix is configured.</returns>
    private static string NormalizePrefix(string prefix)
    {
        if (prefix.Length == 0)
        {
            return string.Empty;
        }

        return prefix.EndsWith('/') ? prefix : $"{prefix}/";
    }
}
