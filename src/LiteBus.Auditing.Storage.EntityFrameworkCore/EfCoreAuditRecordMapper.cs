using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace LiteBus.Auditing.Storage.EntityFrameworkCore;

/// <summary>
///     Maps a LiteBus <see cref="AuditRecord" /> to the <see cref="EfCoreAuditRecord" /> document the Entity
///     Framework Core audit trail stores.
/// </summary>
/// <remarks>
///     The default mapper for the Entity Framework Core audit store: a flattened copy of the record with the
///     actor unfolded into scalar columns, one document per mediated message. The document identity is the
///     record identity, so a redelivered record overwrites rather than duplicates. The chain writer fills the
///     chain fields afterwards, so the mapped document leaves them empty.
/// </remarks>
public sealed class EfCoreAuditRecordMapper : AuditRecordMapper<EfCoreAuditRecord>
{
    /// <inheritdoc />
    public override EfCoreAuditRecord Map(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new EfCoreAuditRecord
        {
            Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
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
                StringComparer.Ordinal),
        };
    }
}
