using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Carries the source identity a recurring loop establishes while a tenant or batch source reads.
/// </summary>
/// <remarks>
///     <para>
///         The loop scopes this context around each source read, inside a root mediation scope, so a query the
///         source sends inherits <see cref="RecurringCommandHostOptions.SourceIdentity" /> through the existing
///         enclosing-mediation rule instead of running anonymously. It is not a mediation: it has no message, no
///         parent, and no cause, and it never leaves the source read.
///     </para>
/// </remarks>
internal sealed class RecurringSourceExecutionContext : IExecutionContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringSourceExecutionContext" /> class.
    /// </summary>
    /// <param name="identity">The identity source reads run under.</param>
    /// <param name="timeProvider">The time provider the handled instant is read from, once.</param>
    /// <param name="cancellationToken">The cancellation token associated with the source read.</param>
    public RecurringSourceExecutionContext(
        ExecutionIdentity identity,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(timeProvider);

        Identity = identity;
        HandledAt = timeProvider.GetUtcNow();
        CancellationToken = cancellationToken;
    }

    /// <inheritdoc />
    public CancellationToken CancellationToken { get; }

    /// <inheritdoc />
    public DateTimeOffset HandledAt { get; }

    /// <inheritdoc />
    public ExecutionIdentity Identity { get; }

    /// <inheritdoc />
    public IDictionary<string, object> Items { get; } = new ConcurrentDictionary<string, object>();

    /// <inheritdoc />
    public IHandleContextData Data { get; } = new HandleContextData();

    /// <inheritdoc />
    public IReadOnlyCollection<string> Tags { get; } = [];

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
