using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a configuration or dependency failure that ends a mediation as a fault.
/// </summary>
/// <remarks>
///     <para>
///         A fault is what an operator must fix: a missing permission lookup, an unknown sign-in instant, a missing
///         mapping, an unreachable store. The mediation reports <see cref="MediationOutcome.Failed" /> with
///         <see cref="Code" />, the fault is logged, and transports answer it as a server error. It is never a
///         refusal, so refusal rewriting cannot turn it into a caller-actionable end.
///     </para>
///     <para>
///         Guards keep returning the two-state <see cref="Verdict" /> at the pipeline boundary; the policy layer maps
///         a fault result to a pipeline fault by raising this exception.
///     </para>
/// </remarks>
public sealed class MessageFaultException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageFaultException" /> class.
    /// </summary>
    /// <param name="messageType">The type of the message being mediated when the fault surfaced.</param>
    /// <param name="code">The stable fault code operators switch on.</param>
    /// <param name="reason">What failed, written for a person.</param>
    public MessageFaultException(Type messageType, string code, string reason)
        : base($"Mediation of '{messageType?.Name}' failed with fault '{code}': {reason}")
    {
        MessageType = messageType;
        Code = code;
        Reason = reason;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageFaultException" /> class, keeping the caught fault as
    ///     the inner exception.
    /// </summary>
    /// <param name="messageType">The type of the message being mediated when the fault surfaced.</param>
    /// <param name="code">The stable fault code operators switch on.</param>
    /// <param name="reason">What failed, written for a person.</param>
    /// <param name="innerException">The fault that was caught.</param>
    public MessageFaultException(Type messageType, string code, string reason, Exception innerException)
        : base($"Mediation of '{messageType?.Name}' failed with fault '{code}': {reason}", innerException)
    {
        MessageType = messageType;
        Code = code;
        Reason = reason;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageFaultException" /> class with a default message.
    /// </summary>
    public MessageFaultException()
        : base("Mediation failed with a fault.")
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageFaultException" /> class with the given message.
    /// </summary>
    /// <param name="message">The message that describes the fault.</param>
    public MessageFaultException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageFaultException" /> class with the given message and
    ///     inner exception.
    /// </summary>
    /// <param name="message">The message that describes the fault.</param>
    /// <param name="innerException">The exception that caused the fault.</param>
    public MessageFaultException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    ///     Gets the type of the message being mediated when the fault surfaced, when known.
    /// </summary>
    public Type? MessageType { get; }

    /// <summary>
    ///     Gets the stable fault code operators switch on, when one was supplied.
    /// </summary>
    public string? Code { get; }

    /// <summary>
    ///     Gets what failed, written for a person, when a reason was supplied.
    /// </summary>
    public string? Reason { get; }
}
