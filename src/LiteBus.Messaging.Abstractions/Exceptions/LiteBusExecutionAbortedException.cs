using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents errors that occur when an execution process is aborted.
///     This exception is typically thrown in scenarios where a process is halted
///     due to failed validations or other conditions that prevent continuation of execution.
/// </summary>
[Serializable]
public class LiteBusExecutionAbortedException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusExecutionAbortedException" /> class.
    /// </summary>
    public LiteBusExecutionAbortedException() : base("LiteBus Execution was aborted.")
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusExecutionAbortedException" /> class with the reason the
    ///     execution was aborted.
    /// </summary>
    /// <param name="reason">The reason supplied by the handler that aborted the execution.</param>
    public LiteBusExecutionAbortedException(string? reason)
        : base(reason is null ? "LiteBus Execution was aborted." : $"LiteBus Execution was aborted. {reason}")
    {
        Reason = reason;
    }

    /// <summary>
    ///     Gets the reason supplied by the handler that aborted the execution, when one was given.
    /// </summary>
    /// <remarks>
    ///     An abort short-circuits the pipeline without reaching post-handlers or error handlers. Recording the reason is
    ///     what allows a completion handler to report why the message ended without being handled.
    /// </remarks>
    public string? Reason { get; }
}
