using System;

namespace LiteBus.Messaging.Abstractions;

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
