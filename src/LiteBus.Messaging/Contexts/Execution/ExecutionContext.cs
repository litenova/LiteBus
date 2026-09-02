using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Contexts.Execution;

/// <inheritdoc cref="IExecutionContext" />
internal sealed class ExecutionContext : IExecutionContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ExecutionContext" /> class with the specified cancellation token.
    /// </summary>
    /// <param name="tags">The tags associated with the execution context.</param>
    /// <param name="items">The key/value collection for sharing data within the execution context.</param>
    /// <param name="cancellationToken">The cancellation token associated with the execution context.</param>
    public ExecutionContext(IEnumerable<string> tags, IDictionary<string, object> items, CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
        Tags = tags.ToList();
        Items = new Dictionary<string, object>(items);
        Data = new HandleContextData();
    }

    /// <inheritdoc />
    public CancellationToken CancellationToken { get; }

    /// <inheritdoc />
    public IDictionary<string, object> Items { get; }

    /// <inheritdoc />
    public IHandleContextData Data { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<string> Tags { get; }

    /// <inheritdoc />
    public object? MessageResult { get; set; }

    /// <inheritdoc />
    public bool PostHandlersSuppressed { get; private set; }

    /// <inheritdoc />
    public void SuppressPostHandlers()
    {
        PostHandlersSuppressed = true;
    }
}
