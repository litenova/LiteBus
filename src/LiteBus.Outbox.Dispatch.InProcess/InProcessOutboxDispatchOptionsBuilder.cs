using System;

namespace LiteBus.Outbox.Dispatch.InProcess;

/// <summary>
///     Collects in-process outbox dispatch settings inside the <c>UseInProcessDispatch</c> configuration action.
/// </summary>
/// <remarks>
///     A mutable builder is used here rather than the immutable <see cref="InProcessOutboxDispatchOptions" /> record
///     so the registration reads as a sequence of acknowledgement calls, which is the shape every other <c>Use*</c>
///     extension in the outbox module uses.
/// </remarks>
public sealed class InProcessOutboxDispatchOptionsBuilder
{
    /// <summary>
    ///     The acknowledgement recorded when envelopes without a handler are allowed, or null while a handler is required.
    /// </summary>
    private string? _missingHandlerReason;

    /// <summary>
    ///     Allows dispatched envelopes that reach no handler to complete as published.
    /// </summary>
    /// <param name="reason">The acknowledgement explaining why publishing to zero handlers is intended.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Without this call a dispatched message with no registered handler fails the attempt, which retries the
    ///     message and eventually dead-letters it. Calling it completes such a dispatch and marks the message
    ///     published without any handler having run. A startup task logs <paramref name="reason" /> at warning
    ///     level so the choice stays visible in production logs.
    /// </remarks>
    public InProcessOutboxDispatchOptionsBuilder AllowEnvelopesWithoutHandler(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        _missingHandlerReason = reason;

        return this;
    }

    /// <summary>
    ///     Creates the immutable options the module registers.
    /// </summary>
    /// <returns>The configured options.</returns>
    internal InProcessOutboxDispatchOptions Build()
    {
        return new InProcessOutboxDispatchOptions
        {
            RequireHandler = _missingHandlerReason is null,
            MissingHandlerReason = _missingHandlerReason
        };
    }
}
