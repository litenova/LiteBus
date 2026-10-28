using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents the denial of a message by a guard or a shortcut, raised when no refusal mapper supplies a value for the caller.
/// </summary>
/// <remarks>
///     <para>
///         A denial is a decision, not a fault, so it does not reach error handlers and is not reported as
///         <see cref="MediationOutcome.Failed" />. The mediation reports <see cref="MediationOutcome.Denied" />, completion
///         handlers observe the denial, and this exception then reaches the caller because a method that must return a
///         value has nothing to return.
///     </para>
///     <para>
///         An application that would rather hand the caller a denial value than raise registers an
///         <see cref="IMessageRefusalMapper{TMessage,TMessageResult}" />. The mapping then lives in one place instead of
///         in every guard, and a guard supplies only the reason and code it knows.
///     </para>
/// </remarks>
public sealed class LiteBusMessageDeniedException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusMessageDeniedException" /> class.
    /// </summary>
    /// <param name="messageType">The type of the message that was denied.</param>
    /// <param name="reason">The reason the guard or shortcut gave for denying the message.</param>
    /// <param name="code">The code the guard or shortcut supplied, when any.</param>
    /// <param name="deniedBy">The guard or shortcut that denied the message, when known.</param>
    public LiteBusMessageDeniedException(Type messageType, string reason, string? code = null, Type? deniedBy = null)
        : base(deniedBy is null
            ? $"Mediation of '{messageType?.Name}' was denied: {reason}"
            : $"Mediation of '{messageType?.Name}' was denied by '{deniedBy.Name}': {reason}")
    {
        MessageType = messageType;
        Reason = reason;
        Code = code;
        DeniedBy = deniedBy;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusMessageDeniedException" /> class for a fault a
    ///     classifier recognized as a denial, keeping the fault as the inner exception.
    /// </summary>
    /// <param name="messageType">The type of the message that was denied.</param>
    /// <param name="reason">The reason the classifier gave for denying the message.</param>
    /// <param name="code">The code the classifier supplied, when any.</param>
    /// <param name="innerException">The fault the classifier recognized.</param>
    public LiteBusMessageDeniedException(Type messageType, string reason, string? code, Exception innerException)
        : base($"Mediation of '{messageType?.Name}' was denied: {reason}", innerException)
    {
        MessageType = messageType;
        Reason = reason;
        Code = code;
        DeniedBy = null;
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
    ///     Gets the type of the message that was refused, when the denial came from a guard or a shortcut.
    /// </summary>
    public Type? MessageType { get; }

    /// <summary>
    ///     Gets the reason the guard or shortcut gave for refusing the message.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    ///     Gets the code the guard or shortcut supplied, when it supplied one.
    /// </summary>
    public string? Code { get; }

    /// <summary>
    ///     Gets the guard or shortcut that denied the message.
    /// </summary>
    /// <value>The concrete guard or shortcut type that denied the message, or <see langword="null" /> when unknown.</value>
    public Type? DeniedBy { get; }
}
