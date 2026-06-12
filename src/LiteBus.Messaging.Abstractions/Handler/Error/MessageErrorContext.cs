using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Carries the message, optional result, and exception observed when an error handler runs.
/// </summary>
public sealed record MessageErrorContext
{
    /// <summary>
    ///     Gets the message that was being processed when the error occurred.
    /// </summary>
    public required object Message { get; init; }

    /// <summary>
    ///     Gets the exception that triggered the error handler.
    /// </summary>
    public required Exception Exception { get; init; }

    /// <summary>
    ///     Gets the result produced before the error, when available.
    /// </summary>
    public object? MessageResult { get; init; }

    /// <summary>
    ///     Creates a typed view of the error context for handler implementations.
    /// </summary>
    /// <typeparam name="TMessage">The message type expected by the handler.</typeparam>
    /// <typeparam name="TMessageResult">The result type expected by the handler.</typeparam>
    /// <returns>A typed error context that shares the same underlying values.</returns>
    public MessageErrorContext<TMessage, TMessageResult> AsTyped<TMessage, TMessageResult>()
        where TMessage : notnull
    {
        return new MessageErrorContext<TMessage, TMessageResult>(
            (TMessage)Message,
            Exception,
            MessageResult is TMessageResult typedResult ? typedResult : default);
    }
}

/// <summary>
///     Typed view of <see cref="MessageErrorContext" /> for handler implementations that prefer compile-time types.
/// </summary>
/// <typeparam name="TMessage">The message type that was being processed when the error occurred.</typeparam>
/// <typeparam name="TMessageResult">The result type produced by the message pipeline, if any.</typeparam>
/// <param name="Message">The message that was being processed when the error occurred.</param>
/// <param name="Exception">The exception that triggered the error handler.</param>
/// <param name="MessageResult">The result produced before the error, when available.</param>
public sealed record MessageErrorContext<TMessage, TMessageResult>(
    TMessage Message,
    Exception Exception,
    TMessageResult? MessageResult)
    where TMessage : notnull;
