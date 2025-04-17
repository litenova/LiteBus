using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Provides a static class for managing the ambient execution context.
/// </summary>
/// <remarks>
///     The ambient execution context is a thread-local storage mechanism that allows the execution context
///     to flow with the logical execution path, even across asynchronous operations. This is achieved using
///     <see cref="AsyncLocal{T}" />, which maintains the context value across async/await points.
///     The ambient execution context is used throughout the LiteBus framework to provide access to the
///     current execution context without having to pass it explicitly through method parameters.
/// </remarks>
public static class AmbientExecutionContext
{
    private static readonly AsyncLocal<IExecutionContext?> ExecutionContextLocal = new();

    /// <summary>
    ///     Gets or sets the current execution context associated with the ambient scope.
    /// </summary>
    /// <remarks>
    ///     This property allows getting or setting the execution context for the current logical execution path.
    ///     When set, the context will be available to all code executing in the same logical execution path,
    ///     even across asynchronous operations.
    ///     Setting this property to null clears the ambient execution context.
    ///     Accessing this property when no context has been set will throw a <see cref="NoExecutionContextException" />.
    /// </remarks>
    /// <exception cref="NoExecutionContextException">Thrown when attempting to get the current context when none has been set.</exception>
    public static IExecutionContext Current
    {
        get => ExecutionContextLocal.Value ?? throw new NoExecutionContextException();
        set => ExecutionContextLocal.Value = value;
    }

    /// <summary>
    ///     Determines whether a current execution context exists.
    /// </summary>
    /// <value>
    ///     <c>true</c> if a current execution context exists; otherwise, <c>false</c>.
    /// </value>
    public static bool HasCurrent => ExecutionContextLocal.Value != null;

    /// <summary>
    ///     Gets the current execution context if it exists, or returns null if no context is set.
    /// </summary>
    /// <returns>The current execution context or null if no context has been set.</returns>
    public static IExecutionContext? GetCurrentOrDefault()
    {
        return ExecutionContextLocal.Value;
    }

    /// <summary>
    ///     Creates a new execution context scope that automatically restores the previous context when disposed.
    /// </summary>
    /// <param name="context">The execution context to use within the scope.</param>
    /// <returns>A disposable scope that restores the previous context when disposed.</returns>
    /// <example>
    ///     <code>
    /// // Create a new execution context
    /// var executionContext = new ExecutionContext(CancellationToken.None);
    /// 
    /// // Use the context within a scope
    /// using (AmbientExecutionContext.CreateScope(executionContext))
    /// {
    ///     // Code here has access to the execution context via AmbientExecutionContext.Current
    ///     var currentContext = AmbientExecutionContext.Current;
    ///     
    ///     // Perform operations with the context
    /// }
    /// // Previous context (if any) is automatically restored here
    /// </code>
    /// </example>
    public static ExecutionContextScope CreateScope(IExecutionContext context)
    {
        return new ExecutionContextScope(context);
    }

    /// <summary>
    ///     Represents a scope for an execution context that automatically restores the previous context when disposed.
    /// </summary>
    /// <remarks>
    ///     This class implements both <see cref="IDisposable" /> and <see cref="IAsyncDisposable" /> to support
    ///     both synchronous and asynchronous usage patterns. When the scope is disposed, the previous execution
    ///     context (if any) is restored.
    /// </remarks>
    public sealed class ExecutionContextScope : IDisposable, IAsyncDisposable
    {
        private readonly IExecutionContext? _previousContext;
        private bool _disposed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ExecutionContextScope" /> class.
        /// </summary>
        /// <param name="newContext">The new execution context to set as current during this scope.</param>
        internal ExecutionContextScope(IExecutionContext newContext)
        {
            _previousContext = GetCurrentOrDefault();
            ExecutionContextLocal.Value = newContext;
        }

        /// <summary>
        ///     Asynchronously disposes the scope and restores the previous execution context.
        /// </summary>
        /// <returns>A <see cref="ValueTask" /> representing the asynchronous dispose operation.</returns>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>
        ///     Disposes the scope and restores the previous execution context.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            // This assignment is safe because ExecutionContextLocal.Value accepts nullable values
            ExecutionContextLocal.Value = _previousContext;
            _disposed = true;
        }
    }
}