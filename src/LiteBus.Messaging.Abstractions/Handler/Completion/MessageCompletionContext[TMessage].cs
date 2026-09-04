using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Provides a typed view of <see cref="MessageCompletionContext" />.
/// </summary>
/// <typeparam name="TMessage">The message type that was processed.</typeparam>
/// <remarks>
///     Use <see cref="MessageCompletionContext{TMessage,TMessageResult}" /> when the message produces a result and the
///     handler needs it typed.
/// </remarks>
public sealed class MessageCompletionContext<TMessage>
    where TMessage : notnull
{
    /// <summary>
    ///     Holds the untyped pipeline context this view exposes.
    /// </summary>
    private readonly MessageCompletionContext _context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageCompletionContext{TMessage}" /> class as a typed view over an
    ///     existing pipeline completion context.
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
    public MediationOutcome Outcome => _context.Outcome;

    /// <summary>
    ///     Gets the result produced by the pipeline, when one was produced before the mediation ended.
    /// </summary>
    public object? MessageResult => _context.MessageResult;

    /// <summary>
    ///     Gets the exception that ended the mediation, when one did.
    /// </summary>
    public Exception? Exception => _context.Exception;

    /// <summary>
    ///     Gets the reason the decision gave for stopping the pipeline, when it stopped.
    /// </summary>
    public string? Reason => _context.Reason;

    /// <summary>
    ///     Gets the machine-readable code the decision gave for stopping the pipeline, when it stopped.
    /// </summary>
    public string? Code => _context.Code;

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
