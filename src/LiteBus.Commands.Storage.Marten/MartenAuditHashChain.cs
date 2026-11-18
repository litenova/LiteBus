using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Computes the SHA-256 chain hash over the canonical JSON of a Marten audit record.
/// </summary>
/// <remarks>
///     <para>
///         The canonical form is a JSON object of string fields in key order: every scalar the document carries,
///         including <see cref="MartenAuditRecord.Segment" />, <see cref="MartenAuditRecord.Sequence" />, and
///         <see cref="MartenAuditRecord.PreviousHash" />, plus each entry of
///         <see cref="MartenAuditRecord.Properties" /> under a <c>property:</c> prefix. Dates use the round-trip
///         format, durations use ticks, and every number uses the invariant culture, so the same document hashes the
///         same on every machine. Only <see cref="MartenAuditRecord.Hash" /> itself is excluded: it is what is being
///         computed.
///     </para>
///     <para>
///         Binding the predecessor hash into every hash is what makes the chain tamper-evident. Altering a record
///         breaks its hash, and recomputing that hash breaks the link the next record carries, so any alteration
///         surfaces at <see cref="MartenAuditTrail.VerifyChainAsync" /> no later than the altered record.
///     </para>
/// </remarks>
public static class MartenAuditHashChain
{
    /// <summary>
    ///     The prefix that carries one entry of <see cref="MartenAuditRecord.Properties" /> in the canonical form.
    /// </summary>
    private const string PropertyKeyPrefix = "property:";

    /// <summary>
    ///     Computes the chain hash for one audit document.
    /// </summary>
    /// <param name="record">The document to hash, with its segment, sequence, and predecessor hash assigned.</param>
    /// <returns>The lowercase hexadecimal SHA-256 over the canonical JSON of the document.</returns>
    public static string ComputeHash(MartenAuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var fields = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["action"] = record.Action,
            ["actor-display-name"] = record.ActorDisplayName,
            ["actor-id"] = record.ActorId,
            ["actor-kind"] = record.ActorKind,
            ["actor-on-behalf-of"] = record.ActorOnBehalfOf,
            ["category"] = record.Category,
            ["correlation-id"] = record.CorrelationId,
            ["duration-ticks"] = record.Duration.Ticks.ToString(CultureInfo.InvariantCulture),
            ["failure-code"] = record.FailureCode,
            ["id"] = record.Id.ToString(),
            ["message-type"] = record.MessageType,
            ["occurred-at"] = record.OccurredAt.ToString("O", CultureInfo.InvariantCulture),
            ["outcome"] = record.Outcome.ToString(),
            ["previous-hash"] = record.PreviousHash,
            ["reason"] = record.Reason,
            ["segment"] = record.Segment,
            ["sequence"] = record.Sequence.ToString(CultureInfo.InvariantCulture),
            ["target-id"] = record.TargetId,
            ["target-kind"] = record.TargetKind,
            ["tenant-id"] = record.TenantId
        };

        foreach (var pair in record.Properties)
        {
            fields[string.Concat(PropertyKeyPrefix, pair.Key)] = pair.Value;
        }

        var canonical = JsonSerializer.Serialize(fields);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));

        return Convert.ToHexStringLower(bytes);
    }
}
