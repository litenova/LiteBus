using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LiteBus.Auditing.Storage.Marten;

/// <summary>
///     Produces the canonical bytes of a <see cref="MartenAuditRecord" /> that the audit hash chain hashes.
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
///         surfaces at <see cref="IAuditChain" /> verification no later than the altered record.
///     </para>
///     <para>
///         The field set is exactly the one the chain hashed before schemes existed, and
///         <see cref="MartenAuditRecord.HashScheme" /> stays out of it, so records written before schemes verify
///         unchanged under <see cref="MartenAuditHashSchemes.Sha256V1" />.
///     </para>
/// </remarks>
public sealed class MartenAuditRecordCanonicalizer : IAuditCanonicalizer<MartenAuditRecord>
{
    /// <summary>
    ///     The prefix that carries one entry of <see cref="MartenAuditRecord.Properties" /> in the canonical form.
    /// </summary>
    private const string PropertyKeyPrefix = "property:";

    /// <inheritdoc />
    public byte[] Canonicalize(MartenAuditRecord document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var fields = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["action"] = document.Action,
            ["actor-display-name"] = document.ActorDisplayName,
            ["actor-id"] = document.ActorId,
            ["actor-kind"] = document.ActorKind,
            ["actor-on-behalf-of"] = document.ActorOnBehalfOf,
            ["category"] = document.Category,
            ["correlation-id"] = document.CorrelationId,
            ["duration-ticks"] = document.Duration.Ticks.ToString(CultureInfo.InvariantCulture),
            ["failure-code"] = document.FailureCode,
            ["id"] = document.Id.ToString(),
            ["message-type"] = document.MessageType,
            ["occurred-at"] = document.OccurredAt.ToString("O", CultureInfo.InvariantCulture),
            ["outcome"] = document.Outcome.ToString(),
            ["previous-hash"] = document.PreviousHash,
            ["reason"] = document.Reason,
            ["segment"] = document.Segment,
            ["sequence"] = document.Sequence.ToString(CultureInfo.InvariantCulture),
            ["target-id"] = document.TargetId,
            ["target-kind"] = document.TargetKind,
            ["tenant-id"] = document.TenantId
        };

        foreach (var pair in document.Properties)
        {
            fields[string.Concat(PropertyKeyPrefix, pair.Key)] = pair.Value;
        }

        var canonical = JsonSerializer.Serialize(fields);

        return Encoding.UTF8.GetBytes(canonical);
    }
}
