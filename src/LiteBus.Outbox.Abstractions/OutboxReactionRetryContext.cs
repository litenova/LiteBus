using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     The input one outbox reaction retry evaluation reads: the failed attempt and the delivery it belongs to.
/// </summary>
/// <param name="Evaluation">
///     The message, identity, dispatch scope, and clock the evaluation resolves settings and reads time from.
/// </param>
/// <param name="Dispatch">The delivery the failed attempt belongs to.</param>
/// <param name="Failure">The exception that ended the attempt.</param>
/// <param name="ReactionAttempt">The 1-based in-dispatch attempt that just failed.</param>
/// <remarks>
///     <para>
///         The dispatcher consults the retry policy only for a classified storage conflict, so a policy never
///         decides over a business refusal or a configuration fault. <see cref="ReactionAttempt" /> counts
///         in-dispatch attempts within this delivery; <see cref="OutboxDispatchContext.AttemptNumber" /> counts
///         store leases across deliveries.
///     </para>
///     <para>
///         <see cref="Evaluation" /> carries the deserialized event as its message. Its execution context is
///         <see langword="null" />: the failed mediation already ended and its scope is gone, so policies read the
///         delivery from <see cref="Dispatch" /> instead of from ambient state.
///     </para>
/// </remarks>
public sealed record OutboxReactionRetryContext(
    MessageEvaluationContext Evaluation,
    OutboxDispatchContext Dispatch,
    Exception Failure,
    int ReactionAttempt);
