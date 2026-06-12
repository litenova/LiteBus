namespace LiteBus.Messaging.Abstractions.Processing;

/// <summary>
///     Defines how durable processors handle failures raised by <c>AfterDispatch</c> hooks after dispatch succeeds.
/// </summary>
public enum ProcessorHookFailurePolicy
{
    /// <summary>
    ///     Moves the message to dead letter when an after-dispatch hook throws.
    /// </summary>
    DeadLetter = 0,

    /// <summary>
    ///     Logs the hook failure and persists the successful dispatch outcome anyway.
    /// </summary>
    CompleteDespiteHookFailure = 1
}
