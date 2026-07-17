using System.Collections.Concurrent;
using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Ambient saga scope used by the inbox processor hook during one envelope dispatch.
/// </summary>
/// <remarks>
///     Scope state is keyed by durable message identifier so parallel dispatches for the same saga correlation retain
///     independent state snapshots. An instance <see cref="AsyncLocal{T}" /> identifies the scope attached to the current
///     asynchronous execution flow.
/// </remarks>
public sealed class SagaExecutionContext : ISagaContext
{
    /// <summary>
    ///     The active dispatch scopes keyed by durable message identifier.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, Scope> _scopes = new();

    /// <summary>
    ///     The durable message identifier for the active scope on the current asynchronous execution flow.
    /// </summary>
    private readonly AsyncLocal<Guid?> _ambientDispatchId = new();

    /// <summary>
    ///     Gets a value indicating whether the saga should be marked completed after dispatch.
    /// </summary>
    internal bool ShouldComplete => GetActiveScope()?.ShouldComplete ?? false;

    /// <summary>
    ///     Gets a value indicating whether state changed during dispatch.
    /// </summary>
    internal bool IsDirty => GetActiveScope()?.IsDirty ?? false;

    /// <summary>
    ///     Gets the optimistic lock version observed when the saga was loaded.
    /// </summary>
    internal int Version => GetActiveScope()?.Version ?? 0;

    /// <inheritdoc />
    public bool IsActive => GetActiveScope() is not null;

    /// <inheritdoc />
    public SagaCorrelation? Correlation => GetActiveScope()?.Correlation;

    /// <inheritdoc />
    public TState GetState<TState>()
        where TState : class, new()
    {
        return (TState)GetRequiredActiveScope().State;
    }

    /// <inheritdoc />
    public void SetState<TState>(TState state)
        where TState : class, new()
    {
        ArgumentNullException.ThrowIfNull(state);

        var scope = GetRequiredActiveScope();
        scope.State = state;
        scope.IsDirty = true;
    }

    /// <inheritdoc />
    public void Complete()
    {
        GetRequiredActiveScope().ShouldComplete = true;
    }

    /// <summary>
    ///     Begins a saga scope for one durable message and state snapshot.
    /// </summary>
    /// <param name="dispatchId">The unique durable message identifier for the dispatch.</param>
    /// <param name="correlation">The saga correlation.</param>
    /// <param name="state">The current state object.</param>
    /// <param name="version">The optimistic lock version.</param>
    internal void Begin(Guid dispatchId, SagaCorrelation correlation, object state, int version)
    {
        if (dispatchId == Guid.Empty)
        {
            throw new ArgumentException("A saga dispatch identifier cannot be empty.", nameof(dispatchId));
        }

        ArgumentNullException.ThrowIfNull(correlation);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfNegative(version);

        if (!_scopes.TryAdd(dispatchId, new Scope(correlation, state, version)))
        {
            throw new InvalidOperationException($"A saga scope is already active for dispatch '{dispatchId}'.");
        }

        _ambientDispatchId.Value = dispatchId;
    }

    /// <summary>
    ///     Re-attaches a previously started dispatch scope for one durable message on the current asynchronous flow.
    /// </summary>
    /// <param name="dispatchId">The unique durable message identifier for the dispatch.</param>
    /// <returns>
    ///     <see langword="true" /> when a scope exists for the message; otherwise, <see langword="false" />.
    /// </returns>
    internal bool TryAttach(Guid dispatchId)
    {
        if (dispatchId == Guid.Empty)
        {
            throw new ArgumentException("A saga dispatch identifier cannot be empty.", nameof(dispatchId));
        }

        if (!_scopes.TryGetValue(dispatchId, out _))
        {
            _ambientDispatchId.Value = null;
            return false;
        }

        _ambientDispatchId.Value = dispatchId;
        return true;
    }

    /// <summary>
    ///     Replaces the loaded state snapshot and version without clearing handler mutations.
    /// </summary>
    /// <param name="state">The state object loaded from the store.</param>
    /// <param name="version">The optimistic lock version loaded from the store.</param>
    internal void RefreshLoadedState(object state, int version)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfNegative(version);

        var scope = GetRequiredActiveScope();
        scope.State = state;
        scope.Version = version;
    }

    /// <summary>
    ///     Clears the active saga scope for the current asynchronous execution flow.
    /// </summary>
    internal void Reset()
    {
        var dispatchId = _ambientDispatchId.Value;

        _ambientDispatchId.Value = null;

        if (dispatchId.HasValue)
        {
            _scopes.TryRemove(dispatchId.Value, out _);
        }
    }

    /// <summary>
    ///     Gets the active state object without a generic constraint.
    /// </summary>
    /// <returns>The active state object.</returns>
    internal object GetActiveState()
    {
        return GetRequiredActiveScope().State;
    }

    /// <summary>
    ///     Gets the active scope for the current asynchronous execution flow.
    /// </summary>
    /// <returns>The active scope when one is attached; otherwise, <see langword="null" />.</returns>
    private Scope? GetActiveScope()
    {
        if (_ambientDispatchId.Value is { } dispatchId && _scopes.TryGetValue(dispatchId, out var attachedScope))
        {
            return attachedScope;
        }

        return null;
    }

    /// <summary>
    ///     Gets the active scope or throws when the current execution flow has not attached one.
    /// </summary>
    /// <returns>The active dispatch scope.</returns>
    private Scope GetRequiredActiveScope()
    {
        return GetActiveScope()
            ?? throw new InvalidOperationException("No saga scope is active for the current dispatch.");
    }

    /// <summary>
    ///     One dispatch-scoped saga state bag.
    /// </summary>
    private sealed class Scope
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Scope" /> class.
        /// </summary>
        /// <param name="correlation">The saga correlation.</param>
        /// <param name="state">The current state object.</param>
        /// <param name="version">The optimistic lock version.</param>
        internal Scope(SagaCorrelation correlation, object state, int version)
        {
            Correlation = correlation;
            State = state;
            Version = version;
        }

        /// <summary>
        ///     Gets the active correlation for this scope.
        /// </summary>
        internal SagaCorrelation Correlation { get; }

        /// <summary>
        ///     Gets or sets the active state object for this scope.
        /// </summary>
        internal object State { get; set; }

        /// <summary>
        ///     Gets or sets the optimistic lock version observed when the saga was loaded.
        /// </summary>
        internal int Version { get; set; }

        /// <summary>
        ///     Gets or sets a value indicating whether the saga should be marked completed after dispatch.
        /// </summary>
        internal bool ShouldComplete { get; set; }

        /// <summary>
        ///     Gets or sets a value indicating whether state changed during dispatch.
        /// </summary>
        internal bool IsDirty { get; set; }
    }
}
