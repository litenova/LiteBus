using System;
using System.Collections.Concurrent;
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
    /// <param name="timeProvider">The time provider the handled instant is read from, once.</param>
    /// <param name="identity">The identity the mediation runs for.</param>
    /// <param name="cancellationToken">The cancellation token associated with the execution context.</param>
    /// <param name="messageType">The runtime type of the message this mediation handles.</param>
    /// <param name="parent">The execution context of the enclosing mediation, or <see langword="null" /> for a root mediation.</param>
    /// <param name="cause">The committed fact that caused this root mediation, or <see langword="null" /> for a nested mediation or an application-started root.</param>
    public ExecutionContext(
        IEnumerable<string> tags,
        IDictionary<string, object> items,
        TimeProvider timeProvider,
        ExecutionIdentity identity,
        CancellationToken cancellationToken,
        Type? messageType = null,
        IExecutionContext? parent = null,
        MediationCause? cause = null)
        : this(tags, items, timeProvider, new Lazy<ExecutionIdentity>(ValidIdentity(identity)), cancellationToken, messageType, parent, cause)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ExecutionContext" /> class with an identity resolved on first
    ///     read.
    /// </summary>
    /// <param name="tags">The tags associated with the execution context.</param>
    /// <param name="items">The key/value collection for sharing data within the execution context.</param>
    /// <param name="timeProvider">The time provider the handled instant is read from, once.</param>
    /// <param name="identity">
    ///     The identity the mediation runs for, resolved the first time a stage reads it, so a mediation that never
    ///     reads it never resolves the identity provider and never opens a dispatch scope for it.
    /// </param>
    /// <param name="cancellationToken">The cancellation token associated with the execution context.</param>
    /// <param name="messageType">The runtime type of the message this mediation handles.</param>
    /// <param name="parent">The execution context of the enclosing mediation, or <see langword="null" /> for a root mediation.</param>
    /// <param name="cause">The committed fact that caused this root mediation, or <see langword="null" /> for a nested mediation or an application-started root.</param>
    public ExecutionContext(
        IEnumerable<string> tags,
        IDictionary<string, object> items,
        TimeProvider timeProvider,
        Lazy<ExecutionIdentity> identity,
        CancellationToken cancellationToken,
        Type? messageType = null,
        IExecutionContext? parent = null,
        MediationCause? cause = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(identity);

        CancellationToken = cancellationToken;
        Tags = tags.ToList();
        Items = new ConcurrentDictionary<string, object>(items);
        Data = new HandleContextData();
        HandledAt = timeProvider.GetUtcNow();
        _identity = identity;
        MessageType = messageType;
        Parent = parent;
        Cause = cause;
    }

    /// <summary>
    ///     The identity the mediation runs for, resolved once on first read and shared by every stage after that.
    /// </summary>
    private readonly Lazy<ExecutionIdentity> _identity;

    /// <inheritdoc />
    public CancellationToken CancellationToken { get; }

    /// <inheritdoc />
    public DateTimeOffset HandledAt { get; }

    /// <inheritdoc />
    public ExecutionIdentity Identity => _identity.Value;

    /// <inheritdoc />
    public Type? MessageType { get; }

    /// <inheritdoc />
    public IExecutionContext? Parent { get; }

    /// <inheritdoc />
    public MediationCause? Cause { get; }

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

    /// <summary>
    ///     Returns the identity after checking it is present, so the eager constructor fails at the call site.
    /// </summary>
    /// <param name="identity">The identity to check.</param>
    /// <returns>The same identity.</returns>
    private static ExecutionIdentity ValidIdentity(ExecutionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return identity;
    }
}
