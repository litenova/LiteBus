namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Decides who an audited action is attributed to.
/// </summary>
/// <remarks>
///     <para>
///         Attribution is the one part of an audit record that LiteBus cannot derive. It knows the action, the outcome,
///         the target and the clock; who is holding the request is the application's to say, and where it lives differs
///         per application: a claim on an authenticated principal, a field on the command, a device key, or the name of
///         the worker that raised it.
///     </para>
///     <para>
///         It runs at the completion stage, which is what makes it the right extension point rather than a pre-stage
///         handler. A denied command produces an audit record, and "who tried" is the most useful thing that record can
///         say, but a pre-handler never runs when a guard denies. Resolving here means attribution survives every path:
///         success, denial, invalid input, failure and cancellation.
///     </para>
///     <para>
///         The context carries the message, so the common case is three lines. A command that names the acting account
///         reads it directly; a command that names none is a worker or a reaction, and its own name is the honest
///         process name.
///     </para>
///     <para>
///         Returning <see langword="null" /> is legitimate and means nothing established an actor. Do not invent one:
///         a fabricated identifier in evidence is worse than a gap that a review can see. Where the application knows
///         the action came from a process, say so with <see cref="AuditActor.System" /> instead.
///     </para>
///     <para>
///         A resolver runs on the completion path of every audited mediation, so keep it free of input and output. It
///         is handed everything it needs, and a resolver that reaches for a database turns each audited message into an
///         extra round trip on the path that also handles failures.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// internal sealed class ActorResolver : IAuditActorResolver
/// {
///     public AuditActor? Resolve(MessageCompletionContext context) => context.Message switch
///     {
///         IActingAccountCommand acting => AuditActor.User(acting.ActingAccountId.ToString()),
///         _ => AuditActor.System(ProcessNameOf(context.Message.GetType()))
///     };
/// }
/// ]]></code>
/// </example>
public interface IAuditActorResolver
{
    /// <summary>
    ///     Resolves the actor an audited action is attributed to.
    /// </summary>
    /// <param name="context">
    ///     How the mediation ended, carrying the message itself so the actor can be read from it.
    /// </param>
    /// <returns>
    ///     The actor, or <see langword="null" /> when nothing established one. An actor supplied by the handler through
    ///     <see cref="IAuditScope.WithActor" /> overrides whatever this returns.
    /// </returns>
    AuditActor? Resolve(MessageCompletionContext context);
}
