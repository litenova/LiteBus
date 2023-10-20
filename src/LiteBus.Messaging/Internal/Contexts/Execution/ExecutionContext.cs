#nullable enable

using System.Collections.Generic;
using System.Threading;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Contexts.Execution;

/// <summary>
/// Represents the execution context for a specific operation or task.
/// </summary>
internal sealed class ExecutionContext : IExecutionContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExecutionContext"/> class with the specified cancellation token.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token associated with the execution context.</param>
    public ExecutionContext(CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// Gets the cancellation token associated with the execution context.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets or sets a key/value collection that can be used to share data within the scope of this execution.
    /// </summary>
    public IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
}