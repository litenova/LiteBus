using System;

namespace LiteBus.Auditing.Anchors.S3;

/// <summary>
///     Builds the object keys checkpoint objects are stored under and the prefixes one segment is listed with.
/// </summary>
internal static class S3CheckpointAnchorKeys
{
    /// <summary>
    ///     The file suffix marking an object as a stored checkpoint.
    /// </summary>
    private const string CheckpointSuffix = ".checkpoint";

    /// <summary>
    ///     Builds the object key one checkpoint is stored under.
    /// </summary>
    /// <param name="keyPrefix">The configured object key prefix.</param>
    /// <param name="segment">The checkpoint segment.</param>
    /// <param name="sequence">The sequence of the last document the checkpoint covers.</param>
    /// <param name="checkpointId">The checkpoint identifier, which keeps the key write-once per checkpoint.</param>
    /// <returns>The object key holding the checkpoint.</returns>
    public static string GetObjectKey(string keyPrefix, string segment, long sequence, Guid checkpointId)
    {
        return $"{GetSegmentPrefix(keyPrefix, segment)}{sequence:D20}-{checkpointId:N}{CheckpointSuffix}";
    }

    /// <summary>
    ///     Builds the object key prefix listing every checkpoint of one segment and nothing else.
    /// </summary>
    /// <param name="keyPrefix">The configured object key prefix.</param>
    /// <param name="segment">The segment to list.</param>
    /// <returns>The prefix matching exactly the objects of <paramref name="segment" />.</returns>
    /// <remarks>
    ///     The segment is escaped, so a slash inside it cannot turn into a virtual directory: without escaping, the
    ///     prefix of segment <c>tenant</c> would also match the objects of segment <c>tenant/2026</c>.
    /// </remarks>
    public static string GetSegmentPrefix(string keyPrefix, string segment)
    {
        return $"{NormalizePrefix(keyPrefix)}{Uri.EscapeDataString(segment)}/";
    }

    /// <summary>
    ///     Determines whether an object key marks a stored checkpoint.
    /// </summary>
    /// <param name="objectKey">The object key to check.</param>
    /// <returns><see langword="true" /> when the object holds a checkpoint; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     Objects without the checkpoint suffix do not belong to this anchor and are ignored when a segment is read
    ///     back, so foreign files under a shared prefix never break a read.
    /// </remarks>
    public static bool IsCheckpointObject(string objectKey)
    {
        return objectKey.EndsWith(CheckpointSuffix, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Normalizes a configured object key prefix to a form object keys can be appended to.
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
