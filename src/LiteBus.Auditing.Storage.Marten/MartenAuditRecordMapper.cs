using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Storage.Marten;

/// <summary>
///     Maps a LiteBus <see cref="AuditRecord" /> to the <see cref="MartenAuditRecord" /> document the Marten audit
///     store keeps.
/// </summary>
/// <remarks>
///     The default mapper the Marten audit store registers: a flattened copy of the record with the actor unfolded
///     into scalar columns, one document per mediated message. The document identity is the record identity, so a
///     record delivered twice overwrites rather than duplicates. The chain writer fills the chain fields afterwards,
///     so the mapped document leaves them empty.
/// </remarks>
public sealed class MartenAuditRecordMapper : IAuditRecordMapper<MartenAuditRecord>
{
    /// <inheritdoc />
    public MartenAuditRecord Map(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new MartenAuditRecord
        {
            Id = record.Id,
            Action = record.Action,
            ActorId = record.Actor?.Id,
            ActorKind = record.Actor?.Kind,
            ActorDisplayName = record.Actor?.DisplayName,
            ActorOnBehalfOf = record.Actor?.OnBehalfOf,
            Outcome = record.Outcome,
            OccurredAt = record.OccurredAt,
            Duration = record.Duration,
            Category = record.Category,
            TargetKind = record.TargetKind,
            TargetId = record.TargetId,
            Reason = record.Reason,
            FailureCode = record.FailureCode,
            MessageType = record.MessageType,
            CorrelationId = record.CorrelationId,
            TenantId = record.TenantId,
            Properties = record.Properties.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal)
        };
    }
}
