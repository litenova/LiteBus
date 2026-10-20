using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Carries the message, outcome, and optional result or exception observed when a mediation operation ends.
/// </summary>
/// <remarks>
///     Completion handlers receive this context exactly once per mediation, on every path: success, answer, denial,
///     invalid input, failure, and cancellation. Unlike <see cref="MessageErrorContext" />, this context is read-only. A
///     completion handler observes the ending; it cannot change it.
/// </remarks>
public sealed class MessageCompletionContext
{
    /// <summary>
    ///     Gets the message that was processed.
    /// </summary>
    public required object Message { get; init; }

    /// <summary>
    ///     Gets the outcome that describes how the mediation ended.
    /// </summary>
    public required MediationOutcome Outcome { get; init; }

    /// <summary>
    ///     Gets the result produced by the pipeline, when one was produced before the mediation ended.
    /// </summary>
    public object? MessageResult { get; init; }

    /// <summary>
    ///     Gets the exception that ended the mediation, when one did.
    /// </summary>
    /// <remarks>
    ///     Present when <see cref="Outcome" /> is <see cref="MediationOutcome.Failed" /> or
    ///     <see cref="MediationOutcome.Canceled" />, and also on a <see cref="MediationOutcome.Denied" /> mediation whose
    ///     refusal was not mapped to a value, where it is the exception the caller receives.
    /// </remarks>
    public Exception? Exception { get; init; }

    /// <summary>
    ///     Gets the reason the decision gave for stopping the pipeline.
    /// </summary>
    /// <remarks>
    ///     Present when <see cref="Outcome" /> is <see cref="MediationOutcome.Denied" />, and when it is
    ///     <see cref="MediationOutcome.Answered" /> and the decision supplied a reason. A stopped mediation reaches
    ///     neither post-handlers nor error handlers, so this is the only description of why the message ended.
    /// </remarks>
    public string? Reason { get; init; }

    /// <summary>
    ///     Gets the machine-readable code the decision gave for stopping the pipeline.
    /// </summary>
    /// <remarks>
    ///     Carries whatever <see cref="Verdict.Deny" />, <see cref="Shortcut.Answer" /> or a single
    ///     <see cref="ValidationFailure" /> supplied, and means the same thing in all three. Switch on this rather than
    ///     matching on <see cref="Reason" />, which is prose written for a person and free to change wording. It is the
    ///     dimension to tag a metric with when counting why messages ended the way they did.
    /// </remarks>
    public string? Code { get; init; }

    /// <summary>
    ///     Gets the elapsed time from the start of mediation until the outcome was observed.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the mediation ended because an exception escaped the pipeline.
    /// </summary>
    /// <remarks>
    ///     A denial is a decision rather than a fault, so it is not reported here even when it reaches the caller as
    ///     <see cref="LiteBusMessageDeniedException" />. Switch on <see cref="Outcome" /> to tell the five endings apart.
    /// </remarks>
    public bool Faulted => Outcome is MediationOutcome.Failed or MediationOutcome.Canceled;

    /// <summary>
    ///     Creates a typed view of the completion context for handler implementations.
    /// </summary>
    /// <typeparam name="TMessage">The message type expected by the handler.</typeparam>
    /// <returns>A typed completion context over this context's state.</returns>
    public MessageCompletionContext<TMessage> AsTyped<TMessage>()
        where TMessage : notnull
    {
        return new MessageCompletionContext<TMessage>(this);
    }

    /// <summary>
    ///     Creates a typed view of the completion context that also exposes the result type.
    /// </summary>
    /// <typeparam name="TMessage">The message type expected by the handler.</typeparam>
    /// <typeparam name="TMessageResult">The result type expected by the handler.</typeparam>
    /// <returns>A typed completion context over this context's state.</returns>
    public MessageCompletionContext<TMessage, TMessageResult> AsTyped<TMessage, TMessageResult>()
        where TMessage : notnull
    {
        return new MessageCompletionContext<TMessage, TMessageResult>(this);
    }
}
