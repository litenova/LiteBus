using System;

namespace LiteBus.Messaging.Abstractions.Authorization;

/// <summary>
///     The input one authorization policy evaluation reads: the message, who the mediation runs for, and where to resolve ports from.
/// </summary>
/// <param name="Message">The message being mediated.</param>
/// <param name="MessageType">The runtime type of the message being mediated.</param>
/// <param name="Identity">Who the mediation runs for: the calling principal and the tenant the work belongs to.</param>
/// <param name="Services">The dispatch scope the evaluation resolves ports such as <see cref="IPermissionLookup" /> from.</param>
/// <param name="HandledAt">The instant the mediation started, read once from the configured time provider.</param>
/// <remarks>
///     <para>
///         Built once per guard decision by the policy guard and handed to every
///         <see cref="IAuthorizationCondition" /> and to the named <see cref="MessagePolicy" />, so each stage of one
///         decision observes the same message, identity, and instant.
///     </para>
///     <para>
///         <see cref="Services" /> is the mediation's dispatch scope, not the application root, so a permission lookup
///         resolved from it observes the same scoped services the handlers do.
///     </para>
/// </remarks>
public sealed record AuthorizationContext(
    object Message,
    Type MessageType,
    ExecutionIdentity Identity,
    IServiceProvider Services,
    DateTimeOffset HandledAt)
{
    /// <summary>
    ///     Gets the execution context of the mediation being authorized.
    /// </summary>
    /// <value>
    ///     The mediation's execution context, or <see langword="null" /> when the guard did not supply one.
    /// </value>
    /// <remarks>
    ///     The policy guard sets this from the ambient execution context before evaluation. Policies that read the
    ///     enclosing mediation, such as <see cref="MessagePolicy.SentBy{TParent}" />, use it; a
    ///     <see langword="null" /> value means the mediation lineage is unavailable, so such a policy denies.
    /// </remarks>
    public IExecutionContext? ExecutionContext { get; init; }
}
