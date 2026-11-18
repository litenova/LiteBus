using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Messaging.Audit;

/// <summary>
///     Holds the audit records of a command batch until the batch outcome is known.
/// </summary>
/// <remarks>
///     <para>
///         A command batch places one buffer in every command's execution context items under <see cref="ItemKey" />.
///         <see cref="AuditRecordWriter" /> finds it there and adds each record, tagged with the batch identifier, instead
///         of writing it to the audit trail. The batch then writes the buffer before its commit, or rewrites it for a
///         rollback and writes it after.
///     </para>
///     <para>
///         The type lives in <c>LiteBus.Messaging</c> because the writer may not reference <c>LiteBus.Commands</c>; the
///         batch reaches it through the assembly's <c>InternalsVisibleTo</c> grant.
///     </para>
/// </remarks>
internal sealed class AuditRecordBuffer
{
    /// <summary>
    ///     The execution context item key under which a command batch places its buffer.
    /// </summary>
    public const string ItemKey = "litebus.batch.audit-buffer";

    /// <summary>
    ///     The audit record property that carries the batch identifier.
    /// </summary>
    public const string BatchIdProperty = "litebus.batch.id";

    /// <summary>
    ///     The failure code given to a record whose command passed but whose batch rolled back.
    /// </summary>
    public const string RolledBackFailureCode = "litebus.batch.rolled_back";

    /// <summary>
    ///     The records added so far, in the order the commands completed.
    /// </summary>
    private readonly List<AuditRecord> _records = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditRecordBuffer" /> class.
    /// </summary>
    /// <param name="batchId">The identifier of the batch, added to every record.</param>
    public AuditRecordBuffer(string batchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        BatchId = batchId;
    }

    /// <summary>
    ///     Gets the identifier of the batch.
    /// </summary>
    public string BatchId { get; }

    /// <summary>
    ///     Gets the buffered records in the order they were added.
    /// </summary>
    public IReadOnlyList<AuditRecord> Records => _records;

    /// <summary>
    ///     Adds a record, tagged with the batch identifier.
    /// </summary>
    /// <param name="record">The record the writer built.</param>
    public void Add(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var properties = new Dictionary<string, string>(record.Properties, StringComparer.Ordinal)
        {
            [BatchIdProperty] = BatchId
        };

        _records.Add(record with { Properties = properties });
    }

    /// <summary>
    ///     Returns the records rewritten for a rolled-back batch: each record that reported success now reports a
    ///     failure with <see cref="RolledBackFailureCode" />, and every other record keeps its own outcome.
    /// </summary>
    /// <returns>The rewritten records, in the order they were added.</returns>
    public IReadOnlyList<AuditRecord> RewriteForRollback()
    {
        var rewritten = new List<AuditRecord>(_records.Count);

        foreach (var record in _records)
        {
            rewritten.Add(record.Outcome == AuditOutcome.Succeeded
                ? record with { Outcome = AuditOutcome.Failed, FailureCode = RolledBackFailureCode }
                : record);
        }

        return rewritten;
    }
}
