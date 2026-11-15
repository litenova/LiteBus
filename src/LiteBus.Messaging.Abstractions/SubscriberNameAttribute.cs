using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Gives an event handler a stable name that per-handler outbox delivery records instead of the handler's full
///     type name.
/// </summary>
/// <remarks>
///     <para>
///         With per-handler delivery, each outbox envelope names the one handler it is addressed to. Without this
///         attribute that name is the handler's full type name, so renaming the class or moving its namespace leaves
///         the envelopes already written addressed to a handler that no longer exists. A subscriber name survives
///         those refactorings.
///     </para>
///     <para>
///         Dispatch matches an envelope to a handler by its subscriber name or its full type name, so envelopes written
///         before the attribute was added still reach the handler. Add the attribute, deploy, let the old envelopes
///         drain, and only then rename the class.
///     </para>
///     <para>
///         The name must be non-empty, at most 200 characters, and use only letters, digits, <c>.</c>, <c>-</c> and
///         <c>_</c>. Two handlers of one event type cannot share a name.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SubscriberNameAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SubscriberNameAttribute" /> class.
    /// </summary>
    /// <param name="name">The stable subscriber name, such as <c>billing.order-created</c>.</param>
    public SubscriberNameAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>
    ///     Gets the stable subscriber name.
    /// </summary>
    public string Name { get; }
}
