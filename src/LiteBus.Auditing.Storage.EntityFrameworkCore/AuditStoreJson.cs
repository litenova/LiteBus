using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Text.Json;

namespace LiteBus.Auditing.Storage.EntityFrameworkCore;

/// <summary>
///     Serializes the JSON columns of the audit tables with deterministic key order.
/// </summary>
internal static class AuditStoreJson
{
    /// <summary>
    ///     The name of the staged-at property the store orders unlinked documents by: a mapped property on
    ///     the default document and a shadow property on a custom document.
    /// </summary>
    internal const string StagedAtPropertyName = "StagedAt";

    /// <summary>
    ///     The shared serializer options used for every audit JSON column.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new();

    /// <summary>
    ///     Serializes segment attributes as a JSON object with keys in ordinal order.
    /// </summary>
    /// <param name="attributes">The attributes to serialize, or <see langword="null" /> for none.</param>
    /// <returns>The JSON object.</returns>
    internal static string SerializeAttributes(IReadOnlyDictionary<string, string>? attributes)
    {
        return SerializeStringMap(attributes);
    }

    /// <summary>
    ///     Reads serialized segment attributes back as a dictionary.
    /// </summary>
    /// <param name="json">The stored JSON object, or <see langword="null" /> for none.</param>
    /// <returns>The attributes.</returns>
    internal static Dictionary<string, string> DeserializeAttributes(string? json)
    {
        return DeserializeStringMap(json);
    }

    /// <summary>
    ///     Serializes record properties as a JSON object with keys in ordinal order.
    /// </summary>
    /// <param name="properties">The properties to serialize.</param>
    /// <returns>The JSON object.</returns>
    internal static string SerializeProperties(IReadOnlyDictionary<string, string> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return SerializeStringMap(properties);
    }

    /// <summary>
    ///     Reads serialized record properties back as a dictionary.
    /// </summary>
    /// <param name="json">The stored JSON object, or <see langword="null" /> for none.</param>
    /// <returns>The properties.</returns>
    internal static Dictionary<string, string> DeserializeProperties(string? json)
    {
        return DeserializeStringMap(json);
    }

    /// <summary>
    ///     Serializes checkpoint signatures as a JSON array in signing order.
    /// </summary>
    /// <param name="signatures">The signatures to serialize, or <see langword="null" /> for none.</param>
    /// <returns>The JSON array.</returns>
    internal static string SerializeSignatures(IReadOnlyList<AuditCheckpointSignature>? signatures)
    {
        if (signatures is null || signatures.Count == 0)
        {
            return "[]";
        }

        return JsonSerializer.Serialize(signatures, SerializerOptions);
    }

    /// <summary>
    ///     Reads serialized checkpoint signatures back as a list.
    /// </summary>
    /// <param name="json">The stored JSON array, or <see langword="null" /> for none.</param>
    /// <returns>The signatures in signing order.</returns>
    internal static IReadOnlyList<AuditCheckpointSignature> DeserializeSignatures(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<AuditCheckpointSignature>>(json, SerializerOptions) ?? [];
    }

    /// <summary>
    ///     Serializes a string map as a JSON object with keys in ordinal order.
    /// </summary>
    /// <param name="values">The values to serialize, or <see langword="null" /> for none.</param>
    /// <returns>The JSON object.</returns>
    private static string SerializeStringMap(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return "{}";
        }

        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in values)
        {
            sorted[pair.Key] = pair.Value;
        }

        return JsonSerializer.Serialize(sorted, SerializerOptions);
    }

    /// <summary>
    ///     Reads a serialized string map back as a dictionary.
    /// </summary>
    /// <param name="json">The stored JSON object, or <see langword="null" /> for none.</param>
    /// <returns>The values.</returns>
    private static Dictionary<string, string> DeserializeStringMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, SerializerOptions)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
