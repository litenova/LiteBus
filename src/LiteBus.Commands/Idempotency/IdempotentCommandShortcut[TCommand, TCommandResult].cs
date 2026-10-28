using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Idempotency;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Commands.Idempotency;

/// <summary>
///     Answers a command whose idempotency key has already been applied, replaying the recorded result.
/// </summary>
/// <typeparam name="TCommand">The command type, bound at registration.</typeparam>
/// <typeparam name="TCommandResult">The result type the command declares, bound at registration.</typeparam>
/// <remarks>
///     <para>
///         Registered by <c>EnableIdempotency</c> alongside the untyped shortcut, which covers the commands that
///         produce no result. The registry closes this one only for commands that declare a result, and the untyped one
///         only for commands that do not, so exactly one applies to any given command.
///     </para>
///     <para>
///         A repeat can only be answered with a value if the first attempt recorded one, which is what
///         <see cref="IdempotencyDeclaration.ReplayResult" /> asks the store to do. Without it, a repeat of an applied
///         result-producing command raises <see cref="PipelineContractException" /> naming the fix: LiteBus will not
///         invent an answer, and the alternative of returning <see langword="default" /> would hand the caller a zero
///         or a null that looks like a real result. Re-running the handler instead would apply an applied key twice,
///         which is what idempotency exists to prevent.
///     </para>
///     <para>
///         A key another delivery is still applying is denied rather than answered, because nobody yet knows what the
///         answer is. The denial carries <see cref="IdempotencyRefusalCodes.InFlight" />, and a repeat whose fingerprint
///         differs from the stored one is denied with
///         <see cref="IdempotencyRefusalCodes.FingerprintMismatch" />.
///     </para>
///     <para>
///         A granted key is recorded in the execution-context data as a <see cref="ClaimedIdempotencyKey" />, so the
///         completion handler settles exactly the claim this mediation holds and never one another delivery holds. The
///         record carries the token fencing the grant, which the store matches before it settles.
///     </para>
/// </remarks>
internal sealed class IdempotentCommandShortcut<TCommand, TCommandResult> : ICommandShortcut<TCommand, TCommandResult>
    where TCommand : ICommand<TCommandResult>
    where TCommandResult : notnull
{
    /// <summary>
    ///     Resolves the scoped key from the command's declaration.
    /// </summary>
    private readonly IdempotencyKeyResolver _keys;

    /// <summary>
    ///     Deserializes a recorded result for replay.
    /// </summary>
    private readonly IMessageSerializer _serializer;

    /// <summary>
    ///     Remembers which keys have been applied.
    /// </summary>
    private readonly IIdempotencyStore _store;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IdempotentCommandShortcut{TCommand, TCommandResult}" /> class.
    /// </summary>
    /// <param name="keys">Resolves the scoped key from the command's declaration.</param>
    /// <param name="store">Remembers which keys have been applied.</param>
    /// <param name="serializer">Deserializes a recorded result for replay.</param>
    public IdempotentCommandShortcut(
        IdempotencyKeyResolver keys,
        IIdempotencyStore store,
        IMessageSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(serializer);

        _keys = keys;
        _store = store;
        _serializer = serializer;
    }

    /// <inheritdoc />
    public async Task<Shortcut<TCommandResult>> TryAnswerAsync(
        TCommand message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var resolved = _keys.Resolve(message);

        if (resolved is null)
        {
            return Shortcut<TCommandResult>.None;
        }

        var claim = await _store
            .TryClaimAsync(resolved.Value.Key, resolved.Value.Fingerprint, cancellationToken)
            .ConfigureAwait(false);

        if (claim.Outcome == IdempotencyClaimOutcome.Granted)
        {
            var claimToken = claim.ClaimToken;

            if (string.IsNullOrWhiteSpace(claimToken))
            {
                throw new PipelineContractException(
                    $"'{typeof(TCommand).Name}' was granted its idempotency key, but the store "
                    + $"'{StoreTypeName}' returned no claim token. A granted claim must carry the token "
                    + "CompleteAsync and ReleaseAsync match before they settle.");
            }

            AmbientExecutionContext.GetCurrentOrDefault()?.Data.Set(new ClaimedIdempotencyKey(
                resolved.Value.Key,
                claimToken,
                resolved.Value.Declaration.ReplayResult));
            return Shortcut<TCommandResult>.None;
        }

        if (claim.Outcome == IdempotencyClaimOutcome.InFlight)
        {
            return Shortcut<TCommandResult>.Deny(
                "the command is still being applied by another delivery; retry after it settles",
                IdempotencyRefusalCodes.InFlight);
        }

        if (claim.Outcome == IdempotencyClaimOutcome.FingerprintMismatch)
        {
            return Shortcut<TCommandResult>.Deny(
                "the command was already applied with different content",
                IdempotencyRefusalCodes.FingerprintMismatch);
        }

        if (claim.Outcome != IdempotencyClaimOutcome.AlreadyCompleted)
        {
            throw new PipelineContractException(
                $"'{typeof(TCommand).Name}' claimed its idempotency key, but the store '{StoreTypeName}' answered "
                + $"with an unrecognized outcome '{claim.Outcome}' ({(int) claim.Outcome}). A store must answer "
                + "Granted, AlreadyCompleted, InFlight, or FingerprintMismatch.");
        }

        if (!resolved.Value.Declaration.ReplayResult)
        {
            throw new PipelineContractException(
                $"'{typeof(TCommand).Name}' declares idempotency and produces a result, but its declaration does not "
                + "set ReplayResult, so there is no answer to give a repeat. Set ReplayResult on the declaration, or "
                + "handle the repeat with a shortcut of your own.");
        }

        if (claim.Payload is null)
        {
            throw new PipelineContractException(
                $"'{typeof(TCommand).Name}' asked for its result to be replayed, but the store returned no recorded "
                + "payload for an applied key. The store must persist the payload passed to CompleteAsync and hand it "
                + "back with the claim.");
        }

        var result = await _serializer
            .DeserializeAsync(typeof(TCommandResult), claim.Payload, cancellationToken)
            .ConfigureAwait(false);

        return Shortcut<TCommandResult>.Answer((TCommandResult) result, "the command was already applied");
    }

    /// <summary>
    ///     Gets the store type name reported when the store breaks its half of the claim contract.
    /// </summary>
    private string StoreTypeName => _store.GetType().FullName ?? _store.GetType().Name;
}
