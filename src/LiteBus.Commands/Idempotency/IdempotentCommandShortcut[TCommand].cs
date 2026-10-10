using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Idempotency;

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
///         answer is. Answering would tell the caller the work is done while it might still fail.
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

        var resolved = _keys.Resolve(message);

        if (resolved is null)
        {
            return Shortcut.None;
        }

        var claim = await _store.TryClaimAsync(resolved.Value.Key, cancellationToken).ConfigureAwait(false);

        return claim.Outcome == IdempotencyClaimOutcome.AlreadyCompleted
            ? Shortcut.Answer("the command was already applied")
            : Shortcut.None;
    }
}
