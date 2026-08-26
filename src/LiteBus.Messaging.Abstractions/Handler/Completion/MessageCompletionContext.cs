using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Carries the message, outcome, and optional result or exception observed when a mediation operation ends.
/// </summary>
/// <remarks>
///     Completion handlers receive this context exactly once per mediation, on every path: success, abort, failure, and
///     cancellation. Unlike <see cref="MessageErrorContext" />, this context is read-only. A completion handler observes
///     the ending; it cannot change it.
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
    public required MessageOutcome Outcome { get; init; }

    /// <summary>
    ///     Gets the result produced by the pipeline, when one was produced before the mediation ended.
    /// </summary>
    public object? MessageResult { get; init; }

    /// <summary>
    ///     Gets the exception that ended the mediation, when <see cref="Outcome" /> is
    ///     <see cref="MessageOutcome.Failed" /> or <see cref="MessageOutcome.Canceled" />.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    ///     Gets the reason supplied to <see cref="IExecutionContext.Abort(object?, string?)" />, when
    ///     <see cref="Outcome" /> is <see cref="MessageOutcome.Aborted" /> and a reason was given.
    /// </summary>
    public string? AbortReason { get; init; }

    /// <summary>
    ///     Gets the elapsed time from the start of mediation until the outcome was observed.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the mediation ended with <see cref="MessageOutcome.Succeeded" />.
    /// </summary>
    public bool IsSuccess => Outcome == MessageOutcome.Succeeded;

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
}
