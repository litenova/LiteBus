using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using Marten;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Writes audit records as Marten documents, staged on the scoped session for successes and out of band otherwise.
/// </summary>
/// <remarks>
///     <para>
///         A record for a successful mediation is staged on the scoped <see cref="IDocumentSession" /> and committed by
///         the same <c>SaveChangesAsync</c> that commits the handler change, so the record and the change are atomic:
///         both commit together and both roll back together. The ordering that makes this work is fixed by priority.
///         The audit completion handler runs at <see cref="HandlerPriorities.Observability" /> and the unit-of-work
///         commit runs at <see cref="HandlerPriorities.UnitOfWork" />, and handlers run in ascending priority order,
///         so the writer always stages before the commit.
///     </para>
///     <para>
///         A record for a denial, failure, cancellation, or invalid message cannot ride that transaction, because the
///         unit of work rolls the session back on exactly those paths. Those records are written out of band on a
///         short session opened from <see cref="IDocumentStore" /> and saved immediately, so a refusal or a fault is
///         still recorded when the change it describes never commits.
///     </para>
///     <para>
///         Registered with the existing messaging <c>UseAuditTrail</c> API, which constructs this trail from the
///         container with its default scoped lifetime. The application registers Marten itself with <c>AddMarten</c>,
///         which supplies both the scoped session and the document store; LiteBus does not register Marten.
///     </para>
/// </remarks>
public sealed class MartenAuditTrail : IAuditTrail
{
    /// <summary>
    ///     Gets the scoped document session that carries staged success records until the unit of work commits.
    /// </summary>
    private readonly IDocumentSession _session;

    /// <summary>
    ///     Gets the document store that opens short sessions for out-of-band non-success records.
    /// </summary>
    private readonly IDocumentStore _documentStore;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditTrail" /> class.
    /// </summary>
    /// <param name="session">The scoped document session the handler staged its documents on.</param>
    /// <param name="documentStore">The application-owned Marten document store.</param>
    public MartenAuditTrail(IDocumentSession session, IDocumentStore documentStore)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(documentStore);

        _session = session;
        _documentStore = documentStore;
    }

    /// <inheritdoc />
    public async Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var document = Map(record);

        if (record.Outcome == AuditOutcome.Succeeded)
        {
            // Staged, not saved: the unit-of-work completion handler runs after the audit writer and commits this
            // session, so the record commits in the same SaveChangesAsync as the handler change.
            _session.Store(document);
            return;
        }

        // The unit of work rolls the handler session back on this path, so staging here would lose the record. A
        // separate short session saves it immediately instead.
        var session = _documentStore.LightweightSession(IsolationLevel.ReadCommitted);

        await using (session.ConfigureAwait(false))
        {
            session.Store(document);
            await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Flattens one audit record into its Marten document.
    /// </summary>
    /// <param name="record">The record produced at the mediation boundary.</param>
    /// <returns>The document staged or written for the record.</returns>
    private static MartenAuditRecord Map(AuditRecord record)
    {
        return new MartenAuditRecord
        {
            Id = Guid.NewGuid(),
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
