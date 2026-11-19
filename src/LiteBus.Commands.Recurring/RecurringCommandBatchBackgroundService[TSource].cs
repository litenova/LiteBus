using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
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
///         source reads outside the sends that follow, so commands must still check the state they change; the lease,
///         not a transaction, keeps two replicas from producing the same batch.
///     </para>
///     <para>
///         An empty batch sends nothing and the loop waits
///         <see cref="RecurringCommandHostOptions.Interval" />. Otherwise the pass follows
///         <see cref="RecurringCommandBatch.Isolation" />. An atomic batch, the default, goes through
///         <see cref="ICommandBatchMediator.TrySendManyAsync" />: a committed batch whose report asked for more work
///         starts the next pass at once, a batch a refusal rolled back is logged once at Warning and the loop waits
///         <see cref="RecurringCommandHostOptions.ErrorBackoff" /> through <see cref="RecurringPassBackoffException" />,
///         and a fault goes through the same conflict and error paths as the single-command loop. Every item of an
///         atomic batch must carry the same identity, or none; a batch that mixes identities fails the pass, because
///         one atomic batch runs under one identity.
///     </para>
///     <para>
///         A per-item batch sends the items one at a time, in list order, each with its own identity: one failing
///         item is logged and counted and never stops the rest of the batch. A refused item is logged once at Warning,
///         an item whose exception <see cref="RecurringCommandHostOptions.IsConcurrencyConflict" /> classifies as a
///         lost race is logged at Information, and any other failure is logged at Error. When any item failed or was
///         refused, the pass asks for <see cref="RecurringCommandHostOptions.ErrorBackoff" /> through
///         <see cref="RecurringPassBackoffException" />, so an item refused on every pass does not spin the loop.
///         Otherwise the pass reports <see cref="RecurringCommandBatch.HasMoreWork" />. An exception never escapes the
///         loop, because a background service that ends on the first transient database blip is a scheduled outage.
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
    ///     Recognizes an optimistic-concurrency conflict thrown by an item send, or null to treat every exception as a
    ///     failure.
    /// </summary>
    private readonly Func<Exception, bool>? _isConcurrencyConflict;

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
    /// <param name="declaration">The registered declaration carrying the lifecycle and timing settings for the loop.</param>
    /// <param name="scopeFactory">Opens the loop scope once, and a fresh scope for the source on each pass.</param>
    /// <param name="timeProvider">Supplies the delay between passes.</param>
    /// <param name="logger">Records pass failures and lifecycle transitions, or null to discard them.</param>
    public RecurringCommandBatchBackgroundService(
        RecurringCommandBatchDeclaration<TSource> declaration,
        IMessageDispatchScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<RecurringCommandBatchBackgroundService<TSource>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _options = declaration.HostOptions;
        _isConcurrencyConflict = declaration.HostOptions.IsConcurrencyConflict;
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
    ///     Resolves the command mediators from the loop's scope and returns the pass that sends one batch from the
    ///     source.
    /// </summary>
    /// <param name="serviceProvider">The service provider of the loop's scope.</param>
    /// <returns>The pass, which reports whether the batch asked for more work.</returns>
    /// <exception cref="LiteBusDependencyResolutionException">
    ///     No <see cref="ICommandMediator" /> or no <see cref="ICommandBatchMediator" /> is registered.
    /// </exception>
    private Func<CancellationToken, Task<bool>> CreatePass(IServiceProvider serviceProvider)
    {
        var mediator = (ICommandMediator?) serviceProvider.GetService(typeof(ICommandMediator));
        if (mediator is null)
        {
            throw new LiteBusDependencyResolutionException(typeof(ICommandMediator));
        }

        var batchMediator = (ICommandBatchMediator?) serviceProvider.GetService(typeof(ICommandBatchMediator));
        if (batchMediator is null)
        {
            throw new LiteBusDependencyResolutionException(typeof(ICommandBatchMediator));
        }

        return cancellationToken => SendBatchOnceAsync(mediator, batchMediator, cancellationToken);
    }

    /// <summary>
    ///     Reads one batch from a fresh source scope and sends it through the mediators resolved when the loop
    ///     started, as one atomic batch or one item at a time according to the batch.
    /// </summary>
    /// <param name="mediator">The command mediator resolved once when the loop started.</param>
    /// <param name="batchMediator">The batch mediator resolved once when the loop started.</param>
    /// <param name="cancellationToken">A token that stops the pass when the host shuts down or the lease is lost.</param>
    /// <returns>
    ///     <see langword="true" /> when the batch sent without failure and reported more work through
    ///     <see cref="IRecurringWorkReport" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="mediator" /> or <paramref name="batchMediator" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="LiteBusDependencyResolutionException">No <typeparamref name="TSource" /> is registered.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="TSource" /> returned no batch.</exception>
    private async Task<bool> SendBatchOnceAsync(
        ICommandMediator mediator,
        ICommandBatchMediator batchMediator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(batchMediator);

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

        var items = batch.Items;

        if (items is null || items.Count == 0)
        {
            return false;
        }

        if (batch.Isolation == RecurringBatchIsolation.PerItem)
        {
            return await SendPerItemAsync(mediator, batch, cancellationToken).ConfigureAwait(false);
        }

        return await SendAtomicAsync(batchMediator, batch, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Sends the batch as one atomic batch through the batch mediator resolved when the loop started.
    /// </summary>
    /// <param name="batchMediator">The batch mediator resolved once when the loop started.</param>
    /// <param name="batch">The batch one pass sends.</param>
    /// <param name="cancellationToken">A token that stops the pass when the host shuts down or the lease is lost.</param>
    /// <returns>
    ///     <see langword="true" /> when the batch committed and reported more work through
    ///     <see cref="IRecurringWorkReport" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="batchMediator" /> or <paramref name="batch" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     The batch holds no command at some index, or its items carry more than one identity.
    /// </exception>
    private async Task<bool> SendAtomicAsync(
        ICommandBatchMediator batchMediator,
        RecurringCommandBatch batch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batchMediator);
        ArgumentNullException.ThrowIfNull(batch);

        var items = batch.Items;
        var sourceName = typeof(TSource).Name;
        var commands = new ICommand[items.Count];
        ExecutionIdentity? identity = null;

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var command = item?.Command;

            if (item is null || command is null)
            {
                throw new InvalidOperationException(
                    $"'{sourceName}' returned no command at index {index}. "
                    + "Return RecurringCommandBatch.Empty when there is nothing to send, and never a null command.");
            }

            commands[index] = command;

            if (item.Identity is null)
            {
                continue;
            }

            if (identity is null)
            {
                identity = item.Identity;
            }
            else if (!identity.Equals(item.Identity))
            {
                throw new InvalidOperationException(
                    $"The batch from '{sourceName}' carries more than one identity, but its isolation is Atomic. "
                    + "Use RecurringBatchIsolation.PerItem to send each item with its own identity.");
            }
        }

        CommandBatchSettings? settings = identity is null
            ? null
            : new CommandBatchSettings { Mediation = new CommandMediationSettings { Identity = identity } };

        var result = await batchMediator
            .TrySendManyAsync(commands, settings, cancellationToken)
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

        RecurringCommandLogMessages.BatchRefused(_logger, sourceName, failedIndex, commandTypeName, refusalCode);

        throw new RecurringPassBackoffException(
            $"The batch from '{sourceName}' was refused at index {failedIndex} ('{commandTypeName}') and rolled back.");
    }

    /// <summary>
    ///     Sends the batch one item at a time through the command mediator resolved when the loop started, each item
    ///     with its own identity.
    /// </summary>
    /// <param name="mediator">The command mediator resolved once when the loop started.</param>
    /// <param name="batch">The batch one pass sends.</param>
    /// <param name="cancellationToken">A token that stops the pass when the host shuts down or the lease is lost.</param>
    /// <returns>
    ///     <see langword="true" /> when no item failed and the batch reported more work through
    ///     <see cref="IRecurringWorkReport" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="mediator" /> or <paramref name="batch" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="InvalidOperationException">The batch holds no command at some index.</exception>
    private async Task<bool> SendPerItemAsync(
        ICommandMediator mediator,
        RecurringCommandBatch batch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediator);
        ArgumentNullException.ThrowIfNull(batch);

        var items = batch.Items;
        var sourceName = typeof(TSource).Name;
        var failures = 0;

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var command = item?.Command;
            var commandTypeName = command?.GetType().Name ?? "unknown";

            try
            {
                if (item is null || command is null)
                {
                    throw new InvalidOperationException(
                        $"'{sourceName}' returned no command at index {index}. "
                        + "Return RecurringCommandBatch.Empty when there is nothing to send, and never a null command.");
                }

                CommandMediationSettings? settings = item.Identity is null
                    ? null
                    : new CommandMediationSettings { Identity = item.Identity };

                await mediator.SendAsync(command, settings, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown and lease loss stop the pass; anything else is one item's failure and stays isolated.
                throw;
            }
            catch (Exception exception) when (_isConcurrencyConflict?.Invoke(exception) == true)
            {
                // Another instance won the race for the same data, so the work this item wanted is already done or
                // will be retried by the next pass. It is logged like a pass conflict and never backs the loop off.
                RecurringCommandLogMessages.PassConflicted(_logger, commandTypeName, exception.GetType().Name);
            }
            catch (LiteBusMessageDeniedException denied)
            {
                RecurringCommandLogMessages.BatchItemRefused(
                    _logger, sourceName, index, commandTypeName, denied.Code ?? "(none)");
                failures++;
            }
            catch (LiteBusMessageInvalidException invalid)
            {
                var code = invalid.Failures.Count == 0 ? "(none)" : invalid.Failures[0].Code ?? "(none)";
                RecurringCommandLogMessages.BatchItemRefused(_logger, sourceName, index, commandTypeName, code);
                failures++;
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception exception)
            {
                // One failing item never stops the rest of the batch: it is logged and counted here, and the pass
                // asks for the error backoff after the last item instead of failing the loop.
                RecurringCommandLogMessages.BatchItemFailed(_logger, sourceName, index, commandTypeName, exception);
                failures++;
            }
#pragma warning restore CA1031
        }

        if (failures > 0)
        {
            RecurringCommandLogMessages.BatchCompletedWithFailures(_logger, sourceName, failures, items.Count);

            throw new RecurringPassBackoffException(
                $"The batch from '{sourceName}' finished with {failures} of {items.Count} items failed.");
        }

        return batch.HasMoreWork;
    }
}
