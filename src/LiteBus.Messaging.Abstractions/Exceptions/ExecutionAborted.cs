using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Represents errors that occur when an execution process is aborted.
/// This exception is typically thrown in scenarios where a process is halted
/// due to failed validations or other conditions that prevent continuation of execution.
/// </summary>
[Serializable]
public class LiteBusExecutionAbortedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LiteBusExecutionAbortedException"/> class.
    /// </summary>
    public LiteBusExecutionAbortedException() : base("LiteBus Execution was aborted.")
    {
    }
}