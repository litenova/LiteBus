using System;
using LiteBus.Messaging.Audit;

namespace LiteBus.Commands.Batching;

/// <summary>
///     Tells the services of one dispatch scope whether they run inside a command batch.
/// </summary>
/// <remarks>
///     <para>
///         Registered as a scoped service. In an ordinary mediation scope it stays inactive, so the unit-of-work,
///         idempotency and audit handlers behave as for a single command. A command batch creates one scope for all of
///         its commands and calls <see cref="Begin" /> on that scope's instance before the first command, which tells
///         those handlers to defer their commit, settlement and writes to the batch.
///     </para>
///     <para>
///         A batch started from a handler inside another batch would get a new scope with an inactive context, so
///         nesting is detected by the ambient marker in <see cref="CommandBatchNesting" />, not here.
///     </para>
/// </remarks>
internal sealed class CommandBatchContext
{
    /// <summary>
    ///     Gets a value indicating whether the scope belongs to a command batch.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    ///     Gets the identifier of the batch, or <see langword="null" /> outside a batch.
    /// </summary>
    public string? BatchId { get; private set; }

    /// <summary>
    ///     Gets the buffer that holds the batch's audit records, or <see langword="null" /> outside a batch.
    /// </summary>
    public AuditRecordBuffer? AuditRecords { get; private set; }

    /// <summary>
    ///     Marks the scope as belonging to a batch.
    /// </summary>
    /// <param name="batchId">The identifier of the batch.</param>
    /// <exception cref="InvalidOperationException">The scope already belongs to a batch.</exception>
    public void Begin(string batchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);

        if (IsActive)
        {
            throw new InvalidOperationException("This dispatch scope already belongs to a command batch.");
        }

        IsActive = true;
        BatchId = batchId;
        AuditRecords = new AuditRecordBuffer(batchId);
    }
}
