#nullable enable

using System.Collections.Generic;
using System.Threading;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Represents the execution context for a specific operation or task.
/// </summary>
public interface IExecutionContext
{
    /// <summary>
    /// Gets the cancellation token associated with the execution context.
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets a key/value collection that can be used to share data within the scope of this execution.
    /// </summary>
    IDictionary<object, object?> Items { get; }

    /// <summary>
    /// Gets the collection of specified tags used to filter message handlers (i.e., pre, main and post) during mediation.
    /// </summary>
    IReadOnlyCollection<string> Tags { get; }

    /// <summary>
    /// The result of the message mediation.
    /// </summary>
    object? MessageResult { get; set; }

    /// <summary>
    /// Aborts the execution of the current mediation execution.
    /// </summary>
    /// <remarks>The messsage result is required if message has specific result and the execution is aborted in pre-handler phase.</remarks>
    void Abort(object? messageResult = null);
}