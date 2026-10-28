using System;

namespace LiteBus.Messaging.Abstractions.DurableMessaging;

/// <summary>
///     Describes idempotency metadata used to detect duplicate durable message submissions.
/// </summary>
/// <remarks>
///     <para>
///         Use <see cref="Idempotency.None" /> when duplicate detection is not required for a message.
///         Use <see cref="Idempotency.Keyed" /> when callers can retry the same logical operation and the store should
///         return the existing row for the same key.
///     </para>
/// </remarks>
public abstract record Idempotency
{
    /// <summary>
    ///     Indicates that no idempotency key is supplied for the message.
    /// </summary>
    public sealed record None : Idempotency
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Idempotency.None" /> class.
        /// </summary>
        private None()
        {
        }

        /// <summary>
        ///     Gets the singleton instance used when duplicate detection is not requested.
        /// </summary>
        public static None Instance { get; } = new();
    }

    /// <summary>
    ///     Carries an application-defined idempotency key stored with the envelope.
    /// </summary>
    /// <param name="Key">The idempotency key used for insert-time deduplication.</param>
    /// <param name="ConflictMode">
    ///     How duplicate keys are handled. Defaults to <see cref="IdempotencyConflictMode.ReturnExisting" />.
    /// </param>
    public sealed record Keyed(string Key, IdempotencyConflictMode ConflictMode = IdempotencyConflictMode.ReturnExisting)
        : Idempotency;

    /// <summary>
    ///     Builds keyed idempotency from the key the in-flight command was granted.
    /// </summary>
    /// <param name="suffix">
    ///     An optional discriminator appended to the claimed key, so one command can key several envelopes distinctly.
    ///     When <see langword="null" />, the claimed key is used unchanged.
    /// </param>
    /// <param name="conflictMode">How duplicate keys are handled.</param>
    /// <returns>The claimed key, suffixed when a suffix was given.</returns>
    /// <exception cref="ArgumentException"><paramref name="suffix" /> is blank.</exception>
    /// <exception cref="InvalidOperationException">
    ///     There is no ambient execution context, or the in-flight command holds no claimed key.
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         Reads the <see cref="ClaimedIdempotencyKey" /> the idempotency shortcut recorded in
    ///         <see cref="IExecutionContext.Data" /> when it granted the in-flight command its claim. A command that
    ///         declares no idempotency key is never granted one, so calling this outside a keyed command's mediation
    ///         throws instead of inventing a key: a generated fallback would make every delivery look distinct and the
    ///         store would accept them all.
    ///     </para>
    ///     <para>
    ///         Nothing reads ambient state unless the caller calls this factory. Writers keep their caller's
    ///         <see cref="None" /> default for envelopes that never ask for the command's key.
    ///     </para>
    /// </remarks>
    public static Keyed FromCommandKey(
        string? suffix = null,
        IdempotencyConflictMode conflictMode = IdempotencyConflictMode.ReturnExisting)
    {
        if (suffix is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(suffix);
        }

        var context = AmbientExecutionContext.GetCurrentOrDefault();

        if (context is null)
        {
            throw new InvalidOperationException(
                "No ambient execution context exists, so there is no in-flight command to take an idempotency key "
                + "from. Call FromCommandKey from inside a command handler, where the mediation has opened a scope.");
        }

        if (!context.Data.TryGet<ClaimedIdempotencyKey>(out var claimed))
        {
            throw new InvalidOperationException(
                "The in-flight command holds no claimed idempotency key. The command declares no idempotency key, or "
                + "the call happens outside the command's mediation.");
        }

        var key = suffix is null ? claimed.Value : $"{claimed.Value}:{EscapeSuffix(suffix)}";

        return new Keyed(key, conflictMode);
    }

    /// <summary>
    ///     Escapes a suffix so the composed durable key cannot collide with a different key and suffix pair.
    /// </summary>
    /// <param name="suffix">The suffix to escape.</param>
    /// <returns>The suffix with backslashes doubled and separators escaped.</returns>
    /// <remarks>
    ///     The composed key stays readable: a suffix without a backslash or a separator passes through unchanged. The
    ///     escaping follows the same backslash scheme the command key resolver uses, so the last raw separator always
    ///     marks the boundary between the claimed key and the suffix.
    /// </remarks>
    private static string EscapeSuffix(string suffix)
    {
        return suffix.Replace("\\", "\\\\").Replace(":", "\\:");
    }
}
