using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Idempotency;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Commands.Idempotency;

/// <summary>
///     Answers a command whose idempotency key has already been applied, for commands that produce no result.
/// </summary>
/// <typeparam name="TCommand">The command type, bound at registration.</typeparam>
/// <remarks>
///     <para>
///         Registered by <c>EnableIdempotency</c>. It runs in the shortcut stage, after guards and validators, so an
///         unauthorized or malformed command cannot claim a key.
///     </para>
///     <para>
///         A command that declares no <see cref="IdempotencyDeclaration" /> passes through untouched, so one
///         registration covers a whole axis and only the declaring commands pay for it.
///     </para>
///     <para>
///         A key another delivery is still applying is denied rather than answered, because nobody yet knows what the
///         answer is. Answering would tell the caller the work is done while it might still fail. The denial carries
///         <see cref="IdempotencyRefusalCodes.InFlight" />, and a repeat whose fingerprint differs from the stored one
///         is denied with <see cref="IdempotencyRefusalCodes.FingerprintMismatch" />.
///     </para>
///     <para>
///         A granted key is recorded in the execution-context data as a <see cref="ClaimedIdempotencyKey" />, so the
///         completion handler settles exactly the claim this mediation holds and never one another delivery holds. The
///         record carries the token fencing the grant, which the store matches before it settles.
///     </para>
///     <para>
///         The registry does not close this shortcut for a command that produces a result, since an untyped shortcut
///         cannot carry a value. <see cref="IdempotentCommandShortcut{TCommand, TCommandResult}" /> covers those.
///     </para>
/// </remarks>
internal sealed class IdempotentCommandShortcut<TCommand> : ICommandShortcut<TCommand>
    where TCommand : ICommand
{
    /// <summary>
    ///     Resolves the scoped key from the command's declaration.
    /// </summary>
    private readonly IdempotencyKeyResolver _keys;

    /// <summary>
    ///     Caches, per runtime command type, whether the command declares a result through <see cref="ICommand{TCommandResult}" />.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, bool> DeclaresResultByType = new();

    /// <summary>
    ///     Remembers which keys have been applied.
    /// </summary>
    private readonly IIdempotencyStore _store;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IdempotentCommandShortcut{TCommand}" /> class.
    /// </summary>
    /// <param name="keys">Resolves the scoped key from the command's declaration.</param>
    /// <param name="store">Remembers which keys have been applied.</param>
    public IdempotentCommandShortcut(IdempotencyKeyResolver keys, IIdempotencyStore store)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(store);

        _keys = keys;
        _store = store;
    }

    /// <inheritdoc />
    public async Task<Shortcut> TryAnswerAsync(TCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // A command that declares a result belongs to the typed shortcut, which can replay that result. This closure can reach
        // one through a base type, and claiming here would make the typed shortcut see its own key as in flight.
        if (DeclaresResultByType.GetOrAdd(message.GetType(), DeclaresResult))
        {
            return Shortcut.None;
        }

        var resolved = _keys.Resolve(message);

        if (resolved is null)
        {
            return Shortcut.None;
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
            return Shortcut.None;
        }

        if (claim.Outcome == IdempotencyClaimOutcome.AlreadyCompleted)
        {
            return Shortcut.Answer("the command was already applied");
        }

        if (claim.Outcome == IdempotencyClaimOutcome.InFlight)
        {
            return Shortcut.Deny(
                "the command is still being applied by another delivery; retry after it settles",
                IdempotencyRefusalCodes.InFlight);
        }

        if (claim.Outcome == IdempotencyClaimOutcome.FingerprintMismatch)
        {
            return Shortcut.Deny(
                "the command was already applied with different content",
                IdempotencyRefusalCodes.FingerprintMismatch);
        }

        throw new PipelineContractException(
            $"'{typeof(TCommand).Name}' claimed its idempotency key, but the store '{StoreTypeName}' answered with "
            + $"an unrecognized outcome '{claim.Outcome}' ({(int) claim.Outcome}). A store must answer Granted, "
            + "AlreadyCompleted, InFlight, or FingerprintMismatch.");
    }

    /// <summary>
    ///     Determines whether a command type declares a result.
    /// </summary>
    /// <param name="commandType">The runtime command type.</param>
    /// <returns><see langword="true" /> when the type implements <see cref="ICommand{TCommandResult}" />.</returns>
    private static bool DeclaresResult(Type commandType)
    {
        return commandType.GetInterfaces().Any(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ICommand<>));
    }

    /// <summary>
    ///     Gets the store type name reported when the store breaks its half of the claim contract.
    /// </summary>
    private string StoreTypeName => _store.GetType().FullName ?? _store.GetType().Name;
}
