using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Batching;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Commands.Idempotency;

/// <summary>
///     Settles the idempotency claims a command batch deferred, once the batch outcome is known.
/// </summary>
/// <remarks>
///     <para>
///         Registered as a scoped <see cref="ICommandBatchParticipant" /> when <c>EnableIdempotency</c> was called. The
///         instance lives exactly as long as one batch, holding the pending settlements its commands added, in the
///         order their completion handlers ran.
///     </para>
///     <para>
///         Inside a batch the <see cref="IdempotencyCompletionHandler" /> never settles per command, for any store. It
///         adds a <see cref="PendingIdempotencySettlement" /> here instead, for every mediation that was granted a
///         claim, whether the command passed or stopped the batch. The batch then settles the whole list at once, so
///         the keys follow the batch outcome rather than each command's.
///     </para>
///     <para>
///         On the commit path an unmarked store is completed in <see cref="BeforeCommitAsync" />, inside the
///         transaction, as a single command completes it before the commit. A store that commits its own writes (see
///         <see cref="ISeparatelyCommittedIdempotencyStore" />) is completed in <see cref="AfterCommitAsync" />, after
///         <see cref="IUnitOfWork.CommitAsync" /> returns: completing it earlier would leave the key marked complete
///         with no data behind it when the commit throws, and the caller's retry would be answered as a repeat. A
///         completion that still fails after its retries is logged and does not fail the batch, because the data is
///         already committed; the key stays claimed until the store's claim timeout, after which a repeat runs again.
///         A pending claim whose outcome is not <see cref="MediationOutcome.Succeeded" /> is released instead of
///         completed, because the work it guarded never applied.
///     </para>
///     <para>
///         On rollback <see cref="RollbackAsync" /> releases every pending claim, for both kinds of store, so a rerun
///         of the same batch executes every handler again. A release that throws is logged and does not replace the
///         failure that stopped the batch.
///     </para>
///     <para>
///         Two commands in one batch that resolve to the same scoped key are refused before the store is touched. The
///         idempotency shortcuts ask <see cref="ContainsKey" /> before <see cref="IIdempotencyStore.TryClaimAsync" />,
///         and the second command is denied with <see cref="IdempotencyRefusalCodes.DuplicateKeyInBatch" />, which
///         stops the batch and rolls it back. The check reads this list rather than the store, so the refusal does
///         not depend on the store's in-progress answer.
///     </para>
///     <para>
///         The batch runs its commands one at a time, and calls the participant steps once each, so the list is only
///         ever touched by one thread at a time.
///     </para>
/// </remarks>
internal sealed class CommandBatchIdempotencyParticipant : ICommandBatchParticipant
{
    /// <summary>
    ///     The delays before the first, second and third retry of a post-commit completion.
    /// </summary>
    private static readonly TimeSpan[] PostCommitRetryDelays =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200)
    ];

    /// <summary>
    ///     Tells whether the scope belongs to a command batch.
    /// </summary>
    private readonly CommandBatchContext _batchContext;

    /// <summary>
    ///     Records post-commit work that failed without failing the batch, such as a completion that kept failing
    ///     after its retries.
    /// </summary>
    private readonly ILogger<CommandBatchIdempotencyParticipant> _logger;

    /// <summary>
    ///     The settlements the batch's commands deferred, in the order their completion handlers ran.
    /// </summary>
    private readonly List<PendingIdempotencySettlement> _pending = [];

    /// <summary>
    ///     Remembers which keys have been applied.
    /// </summary>
    private readonly IIdempotencyStore _store;

    /// <summary>
    ///     Supplies the delay between post-commit completion retries.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandBatchIdempotencyParticipant" /> class.
    /// </summary>
    /// <param name="store">Remembers which keys have been applied.</param>
    /// <param name="batchContext">Tells whether the scope belongs to a command batch.</param>
    /// <param name="timeProvider">Supplies the delay between post-commit completion retries, or <see langword="null" /> for <see cref="TimeProvider.System" />.</param>
    /// <param name="logger">Records post-commit work that failed without failing the batch, or <see langword="null" /> to discard it.</param>
    public CommandBatchIdempotencyParticipant(
        IIdempotencyStore store,
        CommandBatchContext batchContext,
        TimeProvider? timeProvider = null,
        ILogger<CommandBatchIdempotencyParticipant>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(batchContext);

        _store = store;
        _batchContext = batchContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CommandBatchIdempotencyParticipant>.Instance;
    }

    /// <summary>
    ///     Gets a value indicating whether the scope belongs to a command batch.
    /// </summary>
    public bool IsActive => _batchContext.IsActive;

    /// <summary>
    ///     Adds a deferred settlement to the batch.
    /// </summary>
    /// <param name="settlement">The settlement the command's completion handler deferred.</param>
    public void Add(PendingIdempotencySettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);

        _pending.Add(settlement);
    }

    /// <summary>
    ///     Determines whether the batch already holds a deferred settlement for a key.
    /// </summary>
    /// <param name="key">The scoped idempotency key to look for.</param>
    /// <returns><see langword="true" /> when an earlier command in the batch deferred that key; otherwise, <see langword="false" />.</returns>
    public bool ContainsKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        foreach (var settlement in _pending)
        {
            if (string.Equals(settlement.Claimed.Value, key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task BeforeCommitAsync(CancellationToken cancellationToken)
    {
        if (_store is ISeparatelyCommittedIdempotencyStore)
        {
            return;
        }

        foreach (var settlement in _pending)
        {
            if (settlement.Outcome == MediationOutcome.Succeeded)
            {
                await _store.CompleteAsync(settlement.Claimed.Value, settlement.Claimed.ClaimToken, settlement.Payload, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await _store.ReleaseAsync(settlement.Claimed.Value, settlement.Claimed.ClaimToken, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task AfterCommitAsync(CancellationToken cancellationToken)
    {
        if (_store is not ISeparatelyCommittedIdempotencyStore)
        {
            return;
        }

        foreach (var settlement in _pending)
        {
            if (settlement.Outcome == MediationOutcome.Succeeded)
            {
                await CompleteWithRetryAsync(settlement).ConfigureAwait(false);
            }
            else
            {
#pragma warning disable CA1031 // The data is already committed, so a release failure is logged and never fails the batch.
                try
                {
                    await _store.ReleaseAsync(settlement.Claimed.Value, settlement.Claimed.ClaimToken, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    IdempotencySettlementLogMessages.PostCommitReleaseFailed(_logger, settlement.Claimed.Value, exception);
                }
#pragma warning restore CA1031
            }
        }
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        foreach (var settlement in _pending)
        {
#pragma warning disable CA1031 // A release failure must not replace the failure that stopped the batch, so it is logged and dropped.
            try
            {
                await _store.ReleaseAsync(settlement.Claimed.Value, settlement.Claimed.ClaimToken, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                IdempotencySettlementLogMessages.PostCommitReleaseFailed(_logger, settlement.Claimed.Value, exception);
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>
    ///     Completes one deferred claim, retrying a failure three times before logging it.
    /// </summary>
    /// <param name="settlement">The deferred settlement to complete.</param>
    /// <returns>A task representing the asynchronous settlement.</returns>
    /// <remarks>
    ///     A completion that still fails after its retries is logged and does not fail the batch: the data is
    ///     already committed. The key stays claimed until the store's claim timeout, after which a repeat runs again.
    /// </remarks>
    private async Task CompleteWithRetryAsync(PendingIdempotencySettlement settlement)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(PostCommitRetryDelays[attempt - 1], _timeProvider, CancellationToken.None).ConfigureAwait(false);
            }

#pragma warning disable CA1031 // Any failure leaves the key claimed, so every kind is retried the same way.
            try
            {
                await _store.CompleteAsync(settlement.Claimed.Value, settlement.Claimed.ClaimToken, settlement.Payload, CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (Exception exception)
            {
                if (attempt >= PostCommitRetryDelays.Length)
                {
                    IdempotencySettlementLogMessages.PostCommitCompleteFailed(_logger, settlement.Claimed.Value, exception);
                    return;
                }
            }
#pragma warning restore CA1031
        }
    }
}
