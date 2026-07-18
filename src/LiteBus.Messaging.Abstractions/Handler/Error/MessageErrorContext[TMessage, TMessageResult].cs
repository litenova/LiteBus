using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Provides a typed view of <see cref="MessageErrorContext" /> that shares mutable outcome state with the pipeline.
/// </summary>
/// <typeparam name="TMessage">The message type that was being processed when the error occurred.</typeparam>
/// <typeparam name="TMessageResult">The result type produced by the message pipeline, if any.</typeparam>
public sealed class MessageErrorContext<TMessage, TMessageResult>
    where TMessage : notnull
{
    /// <summary>
    ///     Holds the untyped pipeline context whose mutable outcome state this view exposes.
    /// </summary>
    private readonly MessageErrorContext _context;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageErrorContext{TMessage,TMessageResult}" /> class as a typed
    ///     view over an existing pipeline error context.
    /// </summary>
    /// <param name="context">The pipeline error context to wrap.</param>
    internal MessageErrorContext(MessageErrorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>
    ///     Gets the message that was being processed when the error occurred.
    /// </summary>
    public TMessage Message => (TMessage)_context.Message;

    /// <summary>
    ///     Gets the exception that triggered the error handler.
    /// </summary>
    public Exception Exception => _context.Exception;

    /// <summary>
    ///     Gets the result produced before the error, when available.
    /// </summary>
    public TMessageResult? MessageResult => _context.MessageResult is null
        ? default
        : (TMessageResult)_context.MessageResult;

    /// <summary>
    ///     Gets or sets whether the mediation pipeline suppresses the original exception.
    /// </summary>
    public MessageErrorOutcome Outcome
    {
        get => _context.Outcome;
        set => _context.Outcome = value;
    }

    /// <summary>
    ///     Gets or sets the fallback result returned when <see cref="Outcome" /> is
    ///     <see cref="MessageErrorOutcome.Handled" />.
    /// </summary>
    public TMessageResult? HandledResult
    {
        get => _context.HandledResult is null ? default : (TMessageResult)_context.HandledResult;
        set => _context.HandledResult = value;
    }
}
