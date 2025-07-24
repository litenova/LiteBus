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
    /// <param name="cancellationToken">The cancellation token associated with the execution context.</param>
    /// <param name="tags">The tags associated with the execution context.</param>
    public ExecutionContext(CancellationToken cancellationToken, IEnumerable<string> tags)
    {
        CancellationToken = cancellationToken;
        Tags = tags.ToList();
    }

    public CancellationToken CancellationToken { get; }

    public IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

    public IReadOnlyCollection<string> Tags { get; }

    public object? MessageResult { get; set; }

    public void Abort(object? messageResult = null)
    {
        MessageResult = messageResult;
        throw new LiteBusExecutionAbortedException();
    }
}