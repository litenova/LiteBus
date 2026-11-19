using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The input one per-evaluation port reads: the message, who the mediation runs for, where to resolve ports from, and which clock and instant the evaluation observes.
/// </summary>
/// <param name="Message">The message being mediated.</param>
/// <param name="MessageType">The runtime type of the message being mediated.</param>
/// <param name="Identity">Who the mediation runs for: the calling principal and the tenant the work belongs to.</param>
/// <param name="Services">The dispatch scope the evaluation resolves ports from.</param>
/// <param name="Clock">The time provider the evaluation reads the current instant from.</param>
/// <param name="HandledAt">The instant the mediation started, read once from the configured time provider.</param>
/// <remarks>
///     <para>
///         Built once per evaluation and handed to every per-evaluation port, so each stage of one decision observes
///         the same message, identity, clock, and instant. A feature evaluation that needs more than this carries this
///         context plus its own data, rather than inventing a sibling context.
///     </para>
///     <para>
///         <see cref="Services" /> is the mediation's dispatch scope, not the application root, so a port resolved
///         from it observes the same scoped services the handlers do.
///     </para>
///     <para>
///         <see cref="HandledAt" /> is the instant the mediation started; <see cref="Clock" /> reads the current
///         instant for evaluations that compare ages and windows against now.
///     </para>
///     <para>
///         The cancellation token stays a method parameter on the port and never becomes a member of this record, so
///         one context serves evaluations with different lifetimes.
///     </para>
/// </remarks>
public record MessageEvaluationContext(
    object Message,
    Type MessageType,
    ExecutionIdentity Identity,
    IServiceProvider Services,
    TimeProvider Clock,
    DateTimeOffset HandledAt)
{
    /// <summary>
    ///     Gets the execution context of the mediation being evaluated.
    /// </summary>
    /// <value>
    ///     The mediation's execution context, or <see langword="null" /> when the caller did not supply one.
    /// </value>
    /// <remarks>
    ///     The caller sets this from the ambient execution context before evaluation. Ports that read the enclosing
    ///     mediation use it; a <see langword="null" /> value means the mediation lineage is unavailable.
    /// </remarks>
    public IExecutionContext? ExecutionContext { get; init; }
}
