using System;

namespace LiteBus.Messaging.Abstractions.Authorization;

/// <summary>
///     The input one authorization policy evaluation reads: the message, who the mediation runs for, and where to resolve ports from.
/// </summary>
/// <remarks>
///     <para>
///         Built once per guard decision by the policy guard and handed to every
///         <see cref="IAuthorizationCondition" /> and to the named <see cref="MessagePolicy" />, so each stage of one
///         decision observes the same message, identity, clock, and instant.
///     </para>
///     <para>
///         This is the authorization specialization of <see cref="MessageEvaluationContext" />: the same evaluation
///         input every per-evaluation port receives. A port that needs only the shared input takes the base record;
///         authorization policies and conditions take this type.
///     </para>
///     <para>
///         <see cref="MessageEvaluationContext.Services" /> is the mediation's dispatch scope, not the application root, so a permission lookup
///         resolved from it observes the same scoped services the handlers do.
///     </para>
/// </remarks>
public sealed record AuthorizationContext : MessageEvaluationContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuthorizationContext" /> class.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="messageType">The runtime type of the message being mediated.</param>
    /// <param name="identity">Who the mediation runs for: the calling principal and the tenant the work belongs to.</param>
    /// <param name="services">The dispatch scope the evaluation resolves ports such as <see cref="IPermissionLookup" /> from.</param>
    /// <param name="handledAt">The instant the mediation started, read once from the configured time provider.</param>
    /// <remarks>
    ///     The clock defaults to <see cref="TimeProvider.System" />. Use the overload that takes a clock when the
    ///     mediation runs on a configured time provider.
    /// </remarks>
    public AuthorizationContext(
        object message,
        Type messageType,
        ExecutionIdentity identity,
        IServiceProvider services,
        DateTimeOffset handledAt)
        : base(message, messageType, identity, services, TimeProvider.System, handledAt)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(services);
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuthorizationContext" /> class.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="messageType">The runtime type of the message being mediated.</param>
    /// <param name="identity">Who the mediation runs for: the calling principal and the tenant the work belongs to.</param>
    /// <param name="services">The dispatch scope the evaluation resolves ports such as <see cref="IPermissionLookup" /> from.</param>
    /// <param name="clock">The time provider the evaluation reads the current instant from.</param>
    /// <param name="handledAt">The instant the mediation started, read once from the configured time provider.</param>
    public AuthorizationContext(
        object message,
        Type messageType,
        ExecutionIdentity identity,
        IServiceProvider services,
        TimeProvider clock,
        DateTimeOffset handledAt)
        : base(message, messageType, identity, services, clock, handledAt)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(clock);
    }

    /// <summary>
    ///     Creates an authorization context from a message evaluation context.
    /// </summary>
    /// <param name="context">The evaluation context that carries the message, identity, services, clock, and instant.</param>
    /// <returns>The authorization context that carries the same values.</returns>
    public static AuthorizationContext From(MessageEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new AuthorizationContext(
            context.Message,
            context.MessageType,
            context.Identity,
            context.Services,
            context.Clock,
            context.HandledAt)
        {
            ExecutionContext = context.ExecutionContext
        };
    }
}
