#nullable enable
using System.Threading;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Provides a static class for managing the ambient execution context.
/// </summary>
/// <remarks>
/// The ambient execution context is a thread-local storage mechanism that allows the execution context
/// to flow with the logical execution path, even across asynchronous operations. This is achieved using
/// <see cref="AsyncLocal{T}"/>, which maintains the context value across async/await points.
/// 
/// The ambient execution context is used throughout the LiteBus framework to provide access to the
/// current execution context without having to pass it explicitly through method parameters.
/// </remarks>
public static class AmbientExecutionContext
{
    private static readonly AsyncLocal<IExecutionContext?> ExecutionContextLocal = new();

    /// <summary>
    /// Gets or sets the current execution context associated with the ambient scope.
    /// </summary>
    /// <remarks>
    /// This property allows getting or setting the execution context for the current logical execution path.
    /// When set, the context will be available to all code executing in the same logical execution path,
    /// even across asynchronous operations.
    /// 
    /// Setting this property to null clears the ambient execution context.
    /// </remarks>
    public static IExecutionContext? Current
    {
        get => ExecutionContextLocal.Value;
        set => ExecutionContextLocal.Value = value;
    }
}