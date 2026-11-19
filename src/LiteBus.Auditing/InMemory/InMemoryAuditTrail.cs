using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.InMemory;

/// <summary>
///     Records audit records in memory for unit tests.
/// </summary>
/// <remarks>
///     <para>
///         Every written record is appended to a process-local list under one lock, so tests assert what the
///         capture coordinator wrote without a database. The list holds the record instances as written.
///     </para>
///     <para>
///         This trail records only: chain staging in tests goes through
///         <see cref="InMemoryAuditChainStore{TDocument}" /> directly. It is test support, not a storage
///         provider, and must never be registered in an application.
///     </para>
/// </remarks>
public sealed class InMemoryAuditTrail : IAuditTrail
{
    /// <summary>
    ///     The records written so far, in write order. Guarded by the trail lock.
    /// </summary>
    private readonly List<AuditRecord> _records = [];

    /// <summary>
    ///     The lock that serializes writes and snapshots.
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    ///     Gets a snapshot of every record written so far, in write order.
    /// </summary>
    public IReadOnlyList<AuditRecord> Records
    {
        get
        {
            lock (_sync)
            {
                return [.. _records];
            }
        }
    }

    /// <inheritdoc />
    public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _records.Add(record);
        }

        return Task.CompletedTask;
    }
}
