using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares that a message must not be applied twice, and how to recognise a repeat.
/// </summary>
/// <remarks>
///     <para>
///         The key is what makes a repeat recognisable. It has to come from the message rather than from the clock or a
///         counter, because two deliveries of the same intent are the same message and nothing else about them is
///         stable.
///     </para>
///     <para>
///         The selector runs once per mediation, so keep it a pure projection: read fields off the message and format
///         them. It cannot resolve services, because a declaration is created once at registration and takes no
///         dependencies. Anything needing a lookup belongs in a guard, which declares
///         <see cref="KeyedByContext" /> and hands the key forward through <see cref="IExecutionContext.Data" /> as a
///         <see cref="SuppliedIdempotencyKey" />.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class ApplyPaymentCommandDefinition : IIdempotencyDefinition<ApplyPaymentCommand>
/// {
///     public IdempotencyDeclaration Idempotency =>
///         IdempotencyDeclaration.KeyedBy<ApplyPaymentCommand>(command => command.PaymentId.ToString())
///             with { Scope = "payments" };
/// }
/// ]]></code>
/// </example>
public sealed record IdempotencyDeclaration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="IdempotencyDeclaration" /> class.
    /// </summary>
    /// <param name="keySelector">Projects the idempotency key from the message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="keySelector" /> is <see langword="null" />.</exception>
    public IdempotencyDeclaration(Func<object, string> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        KeySelector = keySelector;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="IdempotencyDeclaration" /> class whose key is supplied by a
    ///     pipeline stage rather than projected from the message.
    /// </summary>
    private IdempotencyDeclaration()
    {
    }

    /// <summary>
    ///     Gets the projection from the message to its idempotency key.
    /// </summary>
    /// <value>
    ///     The selector, or <see langword="null" /> when the declaration was created by
    ///     <see cref="KeyedByContext" /> and the key is supplied through the execution context instead.
    /// </value>
    public Func<object, string>? KeySelector { get; init; }

    /// <summary>
    ///     Gets the projection from the message to the fingerprint that distinguishes its content.
    /// </summary>
    /// <value>
    ///     The selector, or <see langword="null" /> when the declaration carries no fingerprint and any repeat of the
    ///     key is answered as already applied. Set it through <see cref="WithFingerprint{TMessage}" />.
    /// </value>
    /// <remarks>
    ///     The fingerprint answers the question the key alone cannot: is this delivery the same request, or a
    ///     different request reusing the key. An HTTP client that retries a payment with the same idempotency key but
    ///     a changed amount is not repeating the first request, and answering it from the stored record would report
    ///     the wrong payment as done. The store keeps the fingerprint the first delivery claimed with and denies a
    ///     repeat whose fingerprint differs, so the caller learns the key is taken rather than receiving another
    ///     request's answer.
    /// </remarks>
    public Func<object, string>? FingerprintSelector { get; init; }

    /// <summary>
    ///     Gets the prefix that separates this message's keys from every other message's.
    /// </summary>
    /// <value>
    ///     The scope, or <see langword="null" /> to use the message type's name. Set it when two message types share
    ///     one key space on purpose, such as a command and the event that replays it.
    /// </value>
    public string? Scope { get; init; }

    /// <summary>
    ///     Gets a value indicating whether a repeat replays the result the first attempt produced.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> to replay the stored result, which requires the result to be serializable.
    ///     <see langword="false" /> (the default) to answer a repeat without a value, which is all a message producing
    ///     no result needs.
    /// </value>
    /// <remarks>
    ///     Replaying costs a serialized copy of every result in the store, and makes the store hold data the message
    ///     produced rather than only the fact that it ran. Turn it on for a message whose caller needs the same answer
    ///     twice, such as an endpoint a client retries; leave it off for a redelivered message nobody is waiting on.
    /// </remarks>
    public bool ReplayResult { get; init; }

    /// <summary>
    ///     Creates a declaration whose key is projected from a message of a known type.
    /// </summary>
    /// <typeparam name="TMessage">The message type the selector reads.</typeparam>
    /// <param name="keySelector">Projects the idempotency key from the message.</param>
    /// <returns>The declaration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keySelector" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The cast inside is safe because a declaration only ever reaches messages assignable to the type it was
    ///     declared for.
    /// </remarks>
    public static IdempotencyDeclaration KeyedBy<TMessage>(Func<TMessage, string> keySelector)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new IdempotencyDeclaration(message => keySelector((TMessage) message));
    }

    /// <summary>
    ///     Creates a declaration whose key an earlier pipeline stage supplies through the execution context.
    /// </summary>
    /// <returns>The declaration.</returns>
    /// <remarks>
    ///     <para>
    ///         Use this when the key cannot be read off the message because deriving it needs services: a signature to
    ///         verify, a payload to parse, a lookup to perform. A guard does that work and stores the result as a
    ///         <see cref="SuppliedIdempotencyKey" /> in <see cref="IExecutionContext.Data" />; guards run before
    ///         shortcuts, so the key is present by the time idempotency asks for it.
    ///     </para>
    ///     <para>
    ///         A message declared this way that reaches the shortcut with no key in context is a configuration error
    ///         and is reported as one. Falling back to a generated key would make every delivery look distinct, which
    ///         is idempotency silently not working.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// public sealed class ProcessorWebhookCommandDefinition : IIdempotencyDefinition<ProcessorWebhookCommand>
    /// {
    ///     public IdempotencyDeclaration Idempotency =>
    ///         IdempotencyDeclaration.KeyedByContext() with { Scope = "processor-webhook" };
    /// }
    ///
    /// public sealed class VerifyProcessorWebhookGuard : ICommandGuard<ProcessorWebhookCommand>
    /// {
    ///     public async Task<Verdict> DecideAsync(ProcessorWebhookCommand message, CancellationToken cancellationToken = default)
    ///     {
    ///         var verified = await _verifier.VerifyAsync(message.RawBody, message.SignatureHeader, cancellationToken);
    ///
    ///         if (verified is null)
    ///         {
    ///             return Verdict.Deny("the webhook signature did not verify");
    ///         }
    ///
    ///         _context.Data.Set(new SuppliedIdempotencyKey(verified.EventId));
    ///         return Verdict.Allow;
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    public static IdempotencyDeclaration KeyedByContext()
    {
        return new IdempotencyDeclaration();
    }

    /// <summary>
    ///     Returns a copy of this declaration that fingerprints the message content.
    /// </summary>
    /// <typeparam name="TMessage">The message type the selector reads.</typeparam>
    /// <param name="fingerprintSelector">Projects the content fingerprint from the message.</param>
    /// <returns>A declaration that claims with the projected fingerprint.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fingerprintSelector" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The cast inside is safe because a declaration only ever reaches messages assignable to the type it was
    ///     declared for. The fingerprint travels with the claim: the first delivery stores it, a repeat with a matching
    ///     fingerprint is answered as already applied, and a repeat with a different one is denied with
    ///     <see cref="IdempotencyRefusalCodes.FingerprintMismatch" />. A declaration without a fingerprint keeps the old
    ///     behavior, where any repeat of the key is answered as already applied.
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// public sealed class ApplyPaymentCommandDefinition : IIdempotencyDefinition<ApplyPaymentCommand>
    /// {
    ///     public IdempotencyDeclaration Idempotency =>
    ///         IdempotencyDeclaration.KeyedBy<ApplyPaymentCommand>(command => command.PaymentId.ToString())
    ///             .WithFingerprint<ApplyPaymentCommand>(command => $"{command.Amount}:{command.Currency}");
    /// }
    /// ]]></code>
    /// </example>
    public IdempotencyDeclaration WithFingerprint<TMessage>(Func<TMessage, string> fingerprintSelector)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(fingerprintSelector);
        return this with { FingerprintSelector = message => fingerprintSelector((TMessage) message) };
    }
}
