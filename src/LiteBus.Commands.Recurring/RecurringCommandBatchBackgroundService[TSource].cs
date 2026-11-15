using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Sends one batch from a source on a fixed interval for the life of the host.
/// </summary>
/// <typeparam name="TSource">The batch source type the loop resolves on each pass.</typeparam>
/// <remarks>
///     <para>
///         One instance per registered batch source. Each pass resolves the source from a fresh dispatch scope, so it
///         takes scoped dependencies exactly as a handler would, and the scope is disposed before the batch is sent. The
///         source reads outside the batch's transaction, so commands must still check the state they change; the lease,
///         not the transaction, keeps two replicas from producing the same batch.
///     </para>
///     <para>
///         An empty batch sends nothing and the loop waits
///         <see cref="RecurringCommandHostOptions.Interval" />. Otherwise the pass sends the batch through
///         <see cref="ICommandBatchMediator.TrySendManyAsync" />:
///         a committed batch whose report asked for more work starts the next pass at once, a batch a refusal rolled back
///         is logged once at Warning and the loop waits
///         <see cref="RecurringCommandHostOptions.ErrorBackoff" /> through <see cref="RecurringPassBackoffException" />,
///         and a fault goes through the same conflict and error paths as the single-command loop. An exception never
///         escapes the loop, because a background service that ends on the first transient database blip is a scheduled
///         outage.
///     </para>
///     <para>
///         When <see cref="RecurringCommandHostOptions.RequireLease" /> is set, each pass first acquires the named
///         <see cref="IRecurringCommandLease" />: a pass that cannot acquire it is skipped and the loop waits
///         <see cref="RecurringCommandHostOptions.Interval" />. One acquired lease covers the whole busy period, so
///         immediate re-sends never let another replica interleave mid-backlog, and it is released before the loop
///         waits. Releasing the lease is part of the pass, so a release failure is logged and backed off like any
///         other pass failure.
///     </para>
///     <para>
///         The lease can be lost mid-pass when another holder takes the row or renewals fail. The handle signals that
///         through <see cref="IRecurringCommandLeaseHandle.LeaseLost" />: the loop ends the busy period, releases the
///         handle, and waits <see cref="RecurringCommandHostOptions.ErrorBackoff" /> instead of sending further passes
///         undefended. The in-flight source read and send run under a token linked to the loss signal, so work that
///         observes cancellation stops with the pass.
///     </para>
/// </remarks>
public sealed class RecurringCommandBatchBackgroundService<TSource> : IBackgroundService
    where TSource : class, IRecurringCommandBatchSource
{
    /// <summary>
    ///     The lifecycle and timing settings for this loop.
    /// </summary>
    private readonly RecurringCommandHostOptions _options;

    /// <summary>
    ///     Opens the loop scope once when the loop starts, and a fresh scope for the source on each pass.
    /// </summary>
    private readonly IMessageDispatchScopeFactory _scopeFactory;

    /// <summary>
    ///     Supplies the delay between passes.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Records pass failures and lifecycle transitions.
    /// </summary>
    private readonly ILogger<RecurringCommandBatchBackgroundService<TSource>> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringCommandBatchBackgroundService{TSource}" /> class.
    /// </summary>
    /// <param name="hostOptions">The lifecycle and timing settings for the loop.</param>
    /// <param name="scopeFactory">Opens the loop scope once, and a fresh scope for the source on each pass.</param>
    /// <param name="timeProvider">Supplies the delay between passes.</param>
    /// <param name="logger">Records pass failures and lifecycle transitions, or null to discard them.</param>
    public RecurringCommandBatchBackgroundService(
        RecurringCommandHostOptions hostOptions,
        IMessageDispatchScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<RecurringCommandBatchBackgroundService<TSource>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(hostOptions);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _options = hostOptions;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<RecurringCommandBatchBackgroundService<TSource>>.Instance;
    }

    /// <inheritdoc />
    public Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loop = new RecurringCommandLoop(
            _options,
            _scopeFactory,
            _timeProvider,
            _logger,
            typeof(TSource).Name,
            typeof(TSource).FullName ?? typeof(TSource).Name);

        return loop.RunAsync(CreatePass, stoppingToken);
    }

    /// <summary>
    ///     Resolves the batch mediator from the loop's scope and returns the pass that sends one batch from the source.
    /// </summary>
    /// <param name="serviceProvider">The service provider of the loop's scope.</param>
    /// <returns>The pass, which reports whether the batch asked for more work.</returns>
    /// <exception cref="LiteBusDependencyResolutionException">No <see cref="ICommandBatchMediator" /> is registered.</exception>
    private Func<CancellationToken, Task<bool>> CreatePass(IServiceProvider serviceProvider)
    {
        var mediator = (ICommandBatchMediator?) serviceProvider.GetService(typeof(ICommandBatchMediator));
        if (mediator is null)
        {
            throw new LiteBusDependencyResolutionException(typeof(ICommandBatchMediator));
        }

        return cancellationToken => SendBatchOnceAsync(mediator, cancellationToken);
    }

    /// <summary>
    ///     Reads one batch from a fresh source scope and sends it through the mediator resolved when the loop started.
    /// </summary>
    /// <param name="mediator">The batch mediator resolved once when the loop started.</param>
    /// <param name="cancellationToken">A token that stops the pass when the host shuts down or the lease is lost.</param>
    /// <returns>
    ///     <see langword="true" /> when the batch committed and reported more work through
    ///     <see cref="IRecurringWorkReport" />.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="mediator" /> is <see langword="null" />.</exception>
    /// <exception cref="LiteBusDependencyResolutionException">No <typeparamref name="TSource" /> is registered.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="TSource" /> returned no batch.</exception>
    private async Task<bool> SendBatchOnceAsync(ICommandBatchMediator mediator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediator);

        RecurringCommandBatch batch;
        var sourceScope = _scopeFactory.CreateScope();
        await using (sourceScope.ConfigureAwait(false))
        {
            var source = (TSource?) sourceScope.ServiceProvider.GetService(typeof(TSource));
            if (source is null)
            {
                throw new LiteBusDependencyResolutionException(typeof(TSource));
            }

            batch = await source.CreateBatchAsync(cancellationToken).ConfigureAwait(false);
        }

        if (batch is null)
        {
            throw new InvalidOperationException(
                $"'{typeof(TSource).Name}' returned no batch from CreateBatchAsync. "
                + "Return RecurringCommandBatch.Empty when there is nothing to send.");
        }

        if (batch.Commands is null || batch.Commands.Count == 0)
        {
            return false;
        }

        var result = await mediator
            .TrySendManyAsync(batch.Commands, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.Outcome == CommandBatchOutcome.Committed)
        {
            return batch.HasMoreWork;
        }

        // TrySendManyAsync throws for faults and failed commits, so a rolled-back result here means a refusal stopped
        // the batch. The refusal is logged once here and the pass asks for the error backoff, so a command refused on
        // every pass does not spin the loop and the loop does not log it a second time as a fault.
        var failedIndex = result.FailedIndex ?? 0;
        var failedEntry = failedIndex >= 0 && failedIndex < result.Entries.Count
            ? result.Entries[failedIndex]
            : null;
        var commandTypeName = failedEntry?.CommandType.Name ?? "unknown";
        var refusalCode = failedEntry?.Result?.Code ?? "(none)";

        RecurringCommandLogMessages.BatchRefused(_logger, typeof(TSource).Name, failedIndex, commandTypeName, refusalCode);

        throw new RecurringPassBackoffException(
            $"The batch from '{typeof(TSource).Name}' was refused at index {failedIndex} ('{commandTypeName}') and rolled back.");
    }
}
