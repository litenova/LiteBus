using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents the refusal of a message by a gate that supplied no result for the caller to receive.
/// </summary>
/// <remarks>
///     <para>
///         A denial is a decision, not a fault, so it does not reach error handlers and is not reported as
///         <see cref="MessageOutcome.Failed" />. The mediation reports <see cref="MessageOutcome.Denied" />, completion
///         handlers observe the denial, and this exception then reaches the caller because a method that must return a
///         value has nothing to return.
///     </para>
///     <para>
///         A gate that would rather hand the caller a refusal value than raise an exception supplies one through
///         <see cref="PipelineDirective{TMessageResult}.Deny(string,TMessageResult)" />.
///     </para>
/// </remarks>
public sealed class LiteBusMessageDeniedException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusMessageDeniedException" /> class.
    /// </summary>
    /// <param name="messageType">The type of the message that was refused.</param>
    /// <param name="reason">The reason the gate gave for refusing the message.</param>
    public LiteBusMessageDeniedException(Type messageType, string reason)
        : base($"Mediation of '{messageType?.Name}' was denied: {reason}")
    {
        MessageType = messageType;
        Reason = reason;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusMessageDeniedException" /> class with a default message.
    /// </summary>
    public LiteBusMessageDeniedException()
        : base("Mediation was denied.")
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusMessageDeniedException" /> class with the given message.
    /// </summary>
    /// <param name="message">The message that describes the denial.</param>
    public LiteBusMessageDeniedException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusMessageDeniedException" /> class with the given message and
    ///     inner exception.
    /// </summary>
    /// <param name="message">The message that describes the denial.</param>
    /// <param name="innerException">The exception that caused the denial.</param>
    public LiteBusMessageDeniedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    ///     Gets the type of the message that was refused, when the denial came from a gate.
    /// </summary>
    public Type? MessageType { get; }

    /// <summary>
    ///     Gets the reason the gate gave for refusing the message, when the denial came from a gate.
    /// </summary>
    public string? Reason { get; }
}
