using System.Collections.Generic;
using System.Threading;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Represents the execution context for a specific operation or task.
/// </summary>
/// <remarks>
/// The execution context provides access to information and services that are relevant to the current
/// execution, such as cancellation tokens, shared data, and tags for filtering handlers.
/// It also provides a mechanism for aborting the execution and setting a result.
/// 
/// The execution context is typically created at the beginning of a mediation operation and is
/// available throughout the entire mediation pipeline, including pre-handlers, main handlers,
/// post-handlers, and error handlers.
/// </remarks>
public interface IExecutionContext
{
    /// <summary>
    /// Gets the cancellation token associated with the execution context.
    /// </summary>
    /// <remarks>
    /// This token can be used to cancel the execution of the current operation.
    /// Handlers should periodically check this token and abort their execution if cancellation is requested.
    /// </remarks>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets a key/value collection that can be used to share data within the scope of this execution.
    /// </summary>
    /// <remarks>
    /// This collection allows handlers to share data with each other during the execution of a single
    /// mediation operation. The data is scoped to the current execution and is not shared across
    /// different mediation operations.
    /// </remarks>
    IDictionary<object, object?> Items { get; }

    /// <summary>
    /// Gets the collection of specified tags used to filter message handlers (i.e., pre, main and post) during mediation.
    /// </summary>
    /// <remarks>
    /// Tags are used to categorize handlers and allow for selective execution of handlers based on the
    /// current execution context. Only handlers with matching tags will be executed during the mediation process.
    /// </remarks>
    IReadOnlyCollection<string> Tags { get; }

    /// <summary>
    /// The result of the message mediation.
    /// </summary>
    /// <remarks>
    /// This property can be set by handlers to provide a result for the mediation operation.
    /// It is typically set by the main handler, but can also be set by pre-handlers or post-handlers
    /// in certain scenarios, such as when aborting the execution.
    /// </remarks>
    object? MessageResult { get; set; }

    /// <summary>
    /// Aborts the execution of the current mediation execution.
    /// </summary>
    /// <param name="messageResult">The message result to set before aborting. This is required if the message has a specific result type and the execution is aborted in the pre-handler phase.</param>
    /// <remarks>
    /// This method allows handlers to abort the execution of the current mediation operation.
    /// When called, the execution is immediately aborted, and no further handlers are executed.
    /// If the message has a specific result type and the execution is aborted in the pre-handler phase,
    /// a message result must be provided to satisfy the result type requirement.
    /// </remarks>
    void Abort(object? messageResult = null);
}