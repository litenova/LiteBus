using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;

namespace LiteBus.Messaging.Processing;

/// <summary>
///     Supplies axis-specific leasing, dispatch, persistence, and telemetry for a pipelined processor pass.
/// </summary>
/// <typeparam name="TEnvelope">The leased envelope type processed by the worker.</typeparam>
/// <typeparam name="TOptions">The processor options type.</typeparam>
internal interface IPipelinedMessageProcessorOperations<TEnvelope, in TOptions>
    where TOptions : ProcessorOptions
{
    /// <summary>
    ///     Gets the warning log template used when lease renewal fails.
    /// </summary>
    string LeaseRenewalFailedMessage { get; }

    /// <summary>
    ///     Gets the debug log template used after a batch is leased.
    /// </summary>
    string LeasedBatchDebugMessage { get; }

    /// <summary>
    ///     Starts the OpenTelemetry activity for one processor pass.
    /// </summary>
    /// <returns>The pass activity, if tracing is enabled.</returns>
    Activity? StartPassActivity();

    /// <summary>
    ///     Records telemetry for newly acquired leases.
    /// </summary>
    /// <param name="count">The number of leased messages.</param>
    void RecordLeasesAcquired(int count);

    /// <summary>
    ///     Records telemetry when an active lease is lost during dispatch.
    /// </summary>
    void RecordLeaseLost();

    /// <summary>
    ///     Leases a batch of pending messages for one pass.
    /// </summary>
    /// <param name="leaseOwner">The worker name assigned to leased messages.</param>
    /// <param name="options">The processor options for the pass.</param>
    /// <param name="now">The current UTC timestamp used for lease expiration.</param>
    /// <param name="cancellationToken">A token used to cancel leasing.</param>
    /// <returns>The leased messages for this pass.</returns>
    Task<IReadOnlyList<TEnvelope>> LeasePendingAsync(
        string leaseOwner,
        TOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Dispatches one leased message.
    /// </summary>
    /// <param name="envelope">The leased message.</param>
    /// <param name="options">The processor options for the pass.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>The post-transition message or <see langword="null" /> when dispatch was canceled.</returns>
    Task<TEnvelope?> DispatchEnvelopeAsync(
        TEnvelope envelope,
        TOptions options,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Persists the terminal outcome for one dispatched message.
    /// </summary>
    /// <param name="sourceEnvelope">The leased message that was dispatched.</param>
    /// <param name="updated">The post-transition message produced by dispatch.</param>
    /// <param name="accumulator">The pass accumulator that collects outcomes from this worker.</param>
    /// <param name="options">The processor options for the pass.</param>
    /// <param name="logger">The logger used for persistence diagnostics.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch-side work.</param>
    /// <returns>A task that completes when hook processing and persistence finish.</returns>
    Task PersistTerminalOutcomeAsync(
        TEnvelope sourceEnvelope,
        TEnvelope updated,
        ConcurrentProcessorPassAccumulator<TEnvelope> accumulator,
        TOptions options,
        ILogger logger,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Builds the pass result and records telemetry for a completed pass.
    /// </summary>
    /// <param name="accumulator">The concurrent pass accumulator that collected outcomes.</param>
    /// <param name="leasedCount">The number of messages leased during the pass.</param>
    /// <param name="elapsed">The wall-clock duration of the pass.</param>
    /// <param name="passActivity">The optional OpenTelemetry activity for the pass.</param>
    /// <param name="logger">The logger used for pass completion diagnostics.</param>
    /// <returns>The processor pass result.</returns>
    ProcessorPassResult FinalizePass(
        ConcurrentProcessorPassAccumulator<TEnvelope> accumulator,
        int leasedCount,
        TimeSpan elapsed,
        Activity? passActivity,
        ILogger logger);

    /// <summary>
    ///     Gets the identifier of one leased message.
    /// </summary>
    /// <param name="envelope">The leased message.</param>
    /// <returns>The message identifier used for lease renewal.</returns>
    Guid GetMessageId(TEnvelope envelope);
}
