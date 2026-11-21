using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents the denial of a nested mediation by a registered <see cref="IMediationNestingRule" />.
/// </summary>
/// <remarks>
///     <para>
///         Thrown by the mediator before the inner pipeline starts, so the denied message never reaches a guard, a
///         handler, or the registry: the send itself was forbidden, not the message. The enclosing handler observes it
///         as a fault from the nested call, which error handlers may recover from like any other fault.
///     </para>
///     <para>
///         <see cref="OuterMessageType" /> is the message type of the nearest enclosing mediation that carries one,
///         normally the direct parent. When an intermediate mediation stands between the inner message and the ancestor
///         the rule objected to, <see cref="Reason" /> names that ancestor.
///     </para>
/// </remarks>
public sealed class NestedMediationException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="NestedMediationException" /> class with a default message.
    /// </summary>
    public NestedMediationException()
        : base("Nested mediation was denied.")
    {
        OuterMessageType = typeof(object);
        InnerMessageType = typeof(object);
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="NestedMediationException" /> class with the given message.
    /// </summary>
    /// <param name="message">The message that describes the denial.</param>
    public NestedMediationException(string message)
        : base(message)
    {
        OuterMessageType = typeof(object);
        InnerMessageType = typeof(object);
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="NestedMediationException" /> class with the given message and
    ///     inner exception.
    /// </summary>
    /// <param name="message">The message that describes the denial.</param>
    /// <param name="innerException">The exception that caused the denial.</param>
    public NestedMediationException(string message, Exception innerException)
        : base(message, innerException)
    {
        OuterMessageType = typeof(object);
        InnerMessageType = typeof(object);
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="NestedMediationException" /> class for a nested mediation a
    ///     rule denied.
    /// </summary>
    /// <param name="outer">The message type of the enclosing mediation.</param>
    /// <param name="inner">The message type of the denied nested mediation.</param>
    /// <param name="reason">Why the rule denied the nested mediation, written for a person.</param>
    /// <param name="code">A machine-readable code a boundary can switch on, when the rule supplied one.</param>
    public NestedMediationException(Type outer, Type inner, string reason, string? code)
        : base($"Mediation of '{inner?.FullName ?? inner?.Name}' nested inside '{outer?.FullName ?? outer?.Name}' was denied: {reason}")
    {
        ArgumentNullException.ThrowIfNull(outer);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        OuterMessageType = outer;
        InnerMessageType = inner;
        Reason = reason;
        Code = code;
    }

    /// <summary>
    ///     Gets the message type of the enclosing mediation.
    /// </summary>
    /// <value>
    ///     The outer message type, or <see cref="object" /> when the exception was created without message types.
    /// </value>
    public Type OuterMessageType { get; }

    /// <summary>
    ///     Gets the message type of the denied nested mediation.
    /// </summary>
    /// <value>
    ///     The inner message type, or <see cref="object" /> when the exception was created without message types.
    /// </value>
    public Type InnerMessageType { get; }

    /// <summary>
    ///     Gets why the rule denied the nested mediation.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    ///     Gets the code the rule supplied, when it supplied one.
    /// </summary>
    public string? Code { get; }
}
