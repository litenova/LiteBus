namespace LiteBus.Events.Abstractions;

/// <summary>
/// Specifies the concurrency behavior for handler execution.
/// </summary>
/// <remarks>
/// This enumeration is used to control whether multiple handlers or priority groups execute 
/// sequentially or concurrently within their execution context.
/// </remarks>
public enum ConcurrencyMode
{
    /// <summary>
    /// Execute sequentially, one after another.
    /// This ensures deterministic execution order and is the current default behavior.
    /// </summary>
    Sequential = 0,

    /// <summary>
    /// Execute in parallel simultaneously.
    /// All applicable handlers or groups run concurrently, which can improve performance
    /// but may introduce non-deterministic execution order.
    /// </summary>
    Parallel = 1
}