using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Provides a typed view of <see cref="MessageCompletionContext" /> that also exposes the result type.
/// </summary>
/// <typeparam name="TMessage">The message type that was processed.</typeparam>
/// <typeparam name="TMessageResult">The result type of the message.</typeparam>
/// <remarks>
///     A completion handler for a message that produces a result gets the result typed here rather than as
///     <see cref="object" />. The result is absent on the paths where the pipeline produced none, which is why it is
///     exposed as a nullable value alongside <see cref="HasResult" />.
/// </remarks>
public sealed class MessageCompletionContext<TMessage, TMessageResult>
    where TMessage : notnull
{
    /// <summary>
    ///     Holds the untyped pipeline context this view exposes.
    /// </summary>
    private readonly MessageCompletionContext _context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageCompletionContext{TMessage,TMessageResult}" /> class as a
    ///     typed view over an existing pipeline completion context.
    /// </summary>
    /// <param name="context">The pipeline completion context to wrap.</param>
    internal MessageCompletionContext(MessageCompletionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>
    ///     Gets the message that was processed.
    /// </summary>
    public TMessage Message => (TMessage) _context.Message;

    /// <summary>
    ///     Gets the outcome that describes how the mediation ended.
    /// </summary>
    public MessageOutcome Outcome => _context.Outcome;

    /// <summary>
    ///     Gets a value indicating whether the pipeline produced a result of the expected type before it ended.
    /// </summary>
    public bool HasResult => _context.MessageResult is TMessageResult;

    /// <summary>
    ///     Gets the result produced by the pipeline, when one of the expected type was produced before the mediation
    ///     ended.
    /// </summary>
    /// <remarks>
    ///     A denial that raised <see cref="LiteBusMessageDeniedException" />, a fault before the main handler ran, and a
    ///     cancellation all end without a result, so this is the default value on those paths.
    /// </remarks>
    public TMessageResult? MessageResult =>
        _context.MessageResult is TMessageResult typed ? typed : default;

    /// <summary>
    ///     Gets the exception that ended the mediation, when one did.
    /// </summary>
    public Exception? Exception => _context.Exception;

    /// <summary>
    ///     Gets the reason the gate gave for stopping the pipeline, when it stopped.
    /// </summary>
    public string? Reason => _context.Reason;

    /// <summary>
    ///     Gets the elapsed time from the start of mediation until the outcome was observed.
    /// </summary>
    public TimeSpan Duration => _context.Duration;

    /// <summary>
    ///     Gets a value indicating whether the mediation ended because an exception escaped the pipeline.
    /// </summary>
    public bool Faulted => _context.Faulted;

    /// <summary>
    ///     Returns the untyped pipeline context this view wraps.
    /// </summary>
    /// <returns>The underlying <see cref="MessageCompletionContext" />.</returns>
    public MessageCompletionContext AsUntyped()
    {
        return _context;
    }
}
