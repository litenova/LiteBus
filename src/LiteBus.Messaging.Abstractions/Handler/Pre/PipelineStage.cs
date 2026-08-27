namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Names the stages of the pre stage, in the order the framework runs them.
/// </summary>
/// <remarks>
///     <para>
///         The value recorded on a descriptor comes from the contract the handler implements, so the framework knows
///         which stage runs a handler before invoking it. That is what lets the order be fixed by construction: priority
///         orders handlers inside a stage and never moves a handler between stages, and neither does the
///         indirect-before-direct rule.
///     </para>
///     <para>
///         The order encodes what each stage may assume about its input. A guard sees every message. A validator sees
///         only messages the caller is allowed to send. A shortcut sees only well-formed messages the caller is allowed
///         to send, so a malformed message cannot claim an idempotency key or collect a cached answer. A pre-handler
///         sees only messages that are going to be handled.
///     </para>
/// </remarks>
public enum PipelineStage
{
    /// <summary>
    ///     Decides whether the message is permitted to proceed. Stops at the first refusal.
    /// </summary>
    Guard = 0,

    /// <summary>
    ///     Decides whether the message is well-formed. Runs every validator and collects their failures.
    /// </summary>
    Validator = 1,

    /// <summary>
    ///     Decides whether the answer is already known. Stops at the first answer.
    /// </summary>
    Shortcut = 2,

    /// <summary>
    ///     Prepares a message that is going to be handled. Cannot stop the pipeline by returning.
    /// </summary>
    PreHandler = 3
}
