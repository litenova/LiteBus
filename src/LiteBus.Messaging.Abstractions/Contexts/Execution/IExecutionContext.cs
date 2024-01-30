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
}