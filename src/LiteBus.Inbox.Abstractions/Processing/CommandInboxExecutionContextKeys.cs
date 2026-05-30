namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Defines execution-context item keys written by inbox dispatchers during processor replay.
/// </summary>
/// <remarks>
///     <para>
///         Dispatch adapters set <see cref="IsInboxExecution" /> to <see langword="true" /> before they execute the
///         deserialized message. They also copy stored correlation, causation, and tenant values into
///         <see cref="LiteBus.Messaging.Abstractions.MessageTraceContextKeys" />. Pre-handlers, post-handlers, and error
///         handlers can read these keys from mediation settings or the ambient execution context when they need different
///         behavior for a message replayed from storage.
///     </para>
///     <para>
///         Use this key only for pipeline policy, logging, metrics, or idempotency checks. Business handlers should stay
///         correct when the key is absent because the same message can still be executed directly through the mediator.
///     </para>
/// </remarks>
public static class InboxExecutionContextKeys
{
    /// <summary>
    ///     Identifies messages currently being executed from an inbox replay.
    /// </summary>
    public const string IsInboxExecution = "__LiteBus.CommandInbox.IsInboxExecution";
}