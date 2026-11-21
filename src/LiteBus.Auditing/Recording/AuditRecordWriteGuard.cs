using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.Logging;

namespace LiteBus.Auditing.Recording;

/// <summary>
///     Hands one audit record to the trail and applies the composition's policy when the write fails.
/// </summary>
/// <remarks>
///     <para>
///         A failed write is logged at error level with the record's action, outcome and tenant, and counted on
///         <see cref="LiteBusAuditingTelemetry.WriteFailuresInstrumentName" />. By default the mediation's own ending
///         stands: a denial still reaches the caller as a denial and a success still commits, without its record.
///     </para>
///     <para>
///         With <see cref="AuditingModuleBuilder.RequireRecord" />, a success whose record cannot be written fails the
///         mediation with <see cref="MessageFaultException" />, so the unit of work does not commit a change that has
///         no record. A record of any other outcome describes a mediation that already ended without a change, so
///         failing it would only replace the answer the caller receives; it is logged and counted either way.
///     </para>
/// </remarks>
internal sealed class AuditRecordWriteGuard
{
    /// <summary>
    ///     The fault code a required record that could not be written ends the mediation with.
    /// </summary>
    internal const string RecordNotWrittenCode = "audit.record-not-written";

    /// <summary>
    ///     Gets the logger the failed writes are reported on, or <see langword="null" /> when none is registered.
    /// </summary>
    private readonly ILogger? _logger;

    /// <summary>
    ///     Gets a value indicating whether a success whose record cannot be written fails the mediation.
    /// </summary>
    private readonly bool _requireRecord;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditRecordWriteGuard" /> class.
    /// </summary>
    /// <param name="logger">The logger the failed writes are reported on, or <see langword="null" />.</param>
    /// <param name="requireRecord">Whether a success whose record cannot be written fails the mediation.</param>
    public AuditRecordWriteGuard(ILogger<AuditRecordWriteGuard>? logger, bool requireRecord)
    {
        _logger = logger;
        _requireRecord = requireRecord;
    }

    /// <summary>
    ///     Writes one record through a trail write and applies the failure policy.
    /// </summary>
    /// <param name="record">The record being written.</param>
    /// <param name="messageType">The type of the message the record describes.</param>
    /// <param name="write">Writes the record to the trail.</param>
    /// <param name="cancellationToken">The token that cancels the write.</param>
    /// <returns>A task that completes when the record is written or its failure is handled.</returns>
    /// <exception cref="MessageFaultException">
    ///     The record of a success could not be written and the composition requires every record.
    /// </exception>
    internal async Task WriteAsync(
        AuditRecord record,
        Type messageType,
        Func<CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(write);

        try
        {
            await write(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A trail can fail with any store or mapper exception; every one is logged, counted and either kept from replacing the mediation's ending or turned into a fault.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            if (_logger is not null)
            {
                AuditingLogMessages.AuditRecordWriteFailed(_logger, record.Action, record.Outcome, record.TenantId, exception);
            }

            AuditingMetrics.RecordWriteFailure(record);

            if (_requireRecord && record.Outcome == AuditOutcome.Succeeded)
            {
                throw new MessageFaultException(
                    messageType,
                    RecordNotWrittenCode,
                    $"The audit record for action '{record.Action}' could not be written, and the composition requires a record for every successful mediation.",
                    exception);
            }
        }
    }
}
