using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     One handler the composition registered for one message: which handler serves which message, in which stage, from
///     which assembly, and how it is delivered.
/// </summary>
/// <param name="MessageType">The message type the handler is linked to.</param>
/// <param name="HandlerType">The handler type, closed over the message for an open generic handler.</param>
/// <param name="Assembly">The simple name of the assembly that defines the handler type.</param>
/// <param name="Stage">
///     The pipeline stage the handler runs in: <c>handler</c>, <c>pre</c> (guards, validators and pre-handlers),
///     <c>post</c>, <c>completion</c> or <c>error</c>.
/// </param>
/// <param name="SubscriberName">
///     The stable subscriber name from <see cref="SubscriberNameAttribute" />, or <see langword="null" /> when the handler
///     declares none.
/// </param>
/// <param name="Delivery">
///     <c>immediate</c> when the handler carries <see cref="DispatchImmediatelyAttribute" />, otherwise <c>default</c>.
/// </param>
/// <param name="Priority">The handler's priority within its stage; lower runs first.</param>
/// <param name="Indirect">
///     <see langword="true" /> when the handler is registered for a base type or interface of the message rather than
///     the message type itself.
/// </param>
/// <remarks>
///     The rules a product enforces over its handlers, such as every event handler naming a subscriber or a module
///     handling only its own messages, read these entries in a composition check instead of scanning assemblies.
/// </remarks>
public sealed record CompositionHandlerEntry(
    Type MessageType,
    Type HandlerType,
    string Assembly,
    string Stage,
    string? SubscriberName,
    string Delivery,
    int Priority,
    bool Indirect);
