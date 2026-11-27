using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares the idempotency position of a message: keyed, so a repeat is recognised and not applied twice, or
///     deliberately exempt.
/// </summary>
/// <remarks>
///     <para>
///         The two positions are separate types rather than one type with a flag, so a declaration cannot hold a
///         combination that means nothing. An exemption has no key; a keyed message has no exemption rationale. Match
///         on <see cref="KeyedIdempotencyDeclaration" /> and <see cref="IdempotencyExemptDeclaration" /> to tell them
///         apart.
///     </para>
///     <para>
///         Declare it with an <c>IIdempotencyDefinition&lt;TMessage&gt;</c> in a definition class beside the message.
///         An exempt declaration satisfies a <c>RequireDeclaration&lt;IdempotencyDeclaration&gt;()</c> requirement and
///         passes the idempotency shortcut untouched, exactly as
///         <c>declarations.Exempt&lt;IdempotencyDeclaration&gt;(rationale)</c> does, so one definition interface covers
///         both positions.
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
///
/// public sealed class RenameVaultCommandDefinition : IIdempotencyDefinition<RenameVaultCommand>
/// {
///     public IdempotencyDeclaration Idempotency =>
///         IdempotencyDeclaration.Exempt("Renaming to the same name twice leaves the same state.");
/// }
/// ]]></code>
/// </example>
public abstract record IdempotencyDeclaration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="IdempotencyDeclaration" /> class.
    /// </summary>
    /// <remarks>
    ///     The hierarchy is closed to the two positions LiteBus defines, so the constructor is not accessible outside
    ///     this assembly.
    /// </remarks>
    internal IdempotencyDeclaration()
    {
    }

    /// <summary>
    ///     Gets a value indicating whether mediating the message claims an idempotency key.
    /// </summary>
    public abstract bool RequiresIdempotency { get; }

    /// <summary>
    ///     Creates a declaration whose key is projected from a message of a known type.
    /// </summary>
    /// <typeparam name="TMessage">The message type the selector reads.</typeparam>
    /// <param name="keySelector">Projects the idempotency key from the message.</param>
    /// <returns>The keyed declaration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keySelector" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The cast inside is safe because a declaration only ever reaches messages assignable to the type it was
    ///     declared for.
    /// </remarks>
    public static KeyedIdempotencyDeclaration KeyedBy<TMessage>(Func<TMessage, string> keySelector)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        return new KeyedIdempotencyDeclaration(message => keySelector((TMessage) message));
    }

    /// <summary>
    ///     Creates a declaration whose key an earlier pipeline stage supplies through the execution context.
    /// </summary>
    /// <returns>The keyed declaration.</returns>
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
    public static KeyedIdempotencyDeclaration KeyedByContext()
    {
        return KeyedIdempotencyDeclaration.ForSuppliedKey();
    }

    /// <summary>
    ///     Creates a declaration for a message that is deliberately exempt from idempotency.
    /// </summary>
    /// <param name="rationale">The recorded reason a repeat of the message needs no recognition.</param>
    /// <returns>The exempt declaration.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rationale" /> is null or whitespace.</exception>
    public static IdempotencyExemptDeclaration Exempt(string rationale)
    {
        return new IdempotencyExemptDeclaration(rationale);
    }
}
