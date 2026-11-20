using System;
using LiteBus.Auditing.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiteBus.Auditing.Anchors.AzureBlob;

/// <summary>
///     Serializes checkpoints to anchor blob text and parses them back.
/// </summary>
internal static class AzureBlobCheckpointCodec
{
    /// <summary>
    ///     The JSON shape anchor blobs are written and read with.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    ///     Serializes a checkpoint to the text stored in its anchor blob.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to serialize.</param>
    /// <returns>The anchor blob text.</returns>
    public static string Serialize(AuditCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        return JsonSerializer.Serialize(
            AzureBlobCheckpointDocument.FromCheckpoint(checkpoint),
            SerializerOptions);
    }

    /// <summary>
    ///     Parses anchor blob text back to a checkpoint.
    /// </summary>
    /// <param name="blobName">The anchor blob the text was read from, named when the text is invalid.</param>
    /// <param name="content">The anchor blob text.</param>
    /// <returns>The checkpoint the blob holds.</returns>
    /// <exception cref="AzureBlobCheckpointAnchorException">
    ///     Thrown when the text is not a checkpoint or was written with an unknown format version.
    /// </exception>
    public static AuditCheckpoint Deserialize(string blobName, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        ArgumentNullException.ThrowIfNull(content);

        AzureBlobCheckpointDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<AzureBlobCheckpointDocument>(content, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new AzureBlobCheckpointAnchorException(
                $"The checkpoint blob '{blobName}' does not hold a checkpoint.",
                exception);
        }

        if (document is null)
        {
            throw new AzureBlobCheckpointAnchorException(
                $"The checkpoint blob '{blobName}' does not hold a checkpoint.");
        }

        if (document.FormatVersion != AzureBlobCheckpointDocument.CurrentFormatVersion)
        {
            throw new AzureBlobCheckpointAnchorException(
                $"The checkpoint blob '{blobName}' was written with format version {document.FormatVersion}, " +
                $"which this package cannot read.");
        }

        return document.ToCheckpoint(blobName);
    }
}
