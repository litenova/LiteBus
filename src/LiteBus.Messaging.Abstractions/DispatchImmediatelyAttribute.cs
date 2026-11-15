using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Marks an event handler, or an event type, whose outbox envelopes are dispatched right after the unit of work
///     commits, with the outbox processor as the fallback.
/// </summary>
/// <remarks>
///     <para>
///         Takes effect only when the outbox module calls <c>EnableImmediateDispatch</c>. With per-handler delivery,
///         put it on the event handler: only that handler's envelope is dispatched immediately. Without per-handler
///         delivery, put it on the event type: the one envelope for the event is dispatched immediately.
///     </para>
///     <para>
///         A successful immediate dispatch marks the envelope published, so the outbox processor never runs it again.
///         A failed one is left to the outbox processor's retry, and the command still succeeds. Handlers stay
///         idempotent, because a crash between a handler's success and the envelope being marked published delivers
///         it again from the outbox.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DispatchImmediatelyAttribute : Attribute;
