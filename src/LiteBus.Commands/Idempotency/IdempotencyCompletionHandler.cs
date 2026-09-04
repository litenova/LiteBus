using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Idempotency;

namespace LiteBus.Commands.Idempotency;

/// <summary>
///     Settles the idempotency claim a command took, once the mediation has ended.
/// </summary>
/// <remarks>
///     <para>
///         Registered by <c>EnableIdempotency</c>. It marks the key applied when the command succeeded and releases it
///         otherwise, so a transient failure does not burn the key and turn the retry into a false repeat.
///     </para>
///     <para>
///         It runs at <see cref="HandlerPriorities.Persistence" />, inside the reserved window, which puts it before an
///         application's commit at <see cref="HandlerPriorities.UnitOfWork" />. A store writing through the same unit of
///         work therefore has its settle staged into the transaction that applies the change, which is what makes the
///         key and the work atomic.
///     </para>
///     <para>
///         An <see cref="MediationOutcome.Answered" /> mediation is left alone. The shortcut answered it because the key
///         was already applied, so there is no claim of its own to settle and completing it again would overwrite the
///         recorded result with nothing.
///     </para>
/// </remarks>
[HandlerPriority(HandlerPriorities.Persistence)]
internal sealed class IdempotencyCompletionHandler : ICommandCompletionHandler
{
    /// <summary>
    ///     Resolves the scoped key from the command's declaration, the same way the shortcut did.
    /// </summary>
    private readonly IdempotencyKeyResolver _keys;

    /// <summary>
    ///     Serializes the result recorded for replay.
    /// </summary>
    private readonly IMessageSerializer _serializer;

    /// <summary>
    ///     Remembers which keys have been applied.
    /// </summary>
    private readonly IIdempotencyStore _store;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IdempotencyCompletionHandler" /> class.
    /// </summary>
    /// <param name="keys">Resolves the scoped key from the command's declaration.</param>
    /// <param name="store">Remembers which keys have been applied.</param>
    /// <param name="serializer">Serializes the result recorded for replay.</param>
    public IdempotencyCompletionHandler(
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
    public async Task HandleCompletionAsync(
        MessageCompletionContext<ICommand> context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Outcome == MediationOutcome.Answered)
        {
            return;
        }

        var resolved = _keys.Resolve(context.Message);

        if (resolved is null)
        {
            return;
        }

        if (context.Outcome != MediationOutcome.Succeeded)
        {
            await _store.ReleaseAsync(resolved.Value.Key, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        var payload = resolved.Value.Declaration.ReplayResult && context.MessageResult is not null
            ? await _serializer.SerializeAsync(context.MessageResult, CancellationToken.None).ConfigureAwait(false)
            : null;

        await _store.CompleteAsync(resolved.Value.Key, payload, CancellationToken.None).ConfigureAwait(false);
    }
}
