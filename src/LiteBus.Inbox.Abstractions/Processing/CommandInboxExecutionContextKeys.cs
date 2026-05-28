namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Defines execution-context item keys written by the inbox processor.
/// </summary>
/// <remarks>
///     <para>
///         The inbox processor sets <see cref="IsInboxExecution" /> to <see langword="true" /> before it calls
///         `ICommandMediator.SendAsync`. Command pre-handlers, post-handlers, and error handlers can read this key from
///         `CommandMediationSettings.Items` or the ambient execution context when they need different behavior for a
///         command replayed from storage.
///     </para>
///     <para>
///         Use this key only for pipeline policy, logging, metrics, or idempotency checks. Business handlers should stay
///         correct when the key is absent because the same command can still be executed directly through the mediator.
///     </para>
/// </remarks>
public static class CommandInboxExecutionContextKeys
{
    /// <summary>
    ///     Identifies commands currently being executed by the inbox processor.
    /// </summary>
    public const string IsInboxExecution = "__LiteBus.CommandInbox.IsInboxExecution";
}