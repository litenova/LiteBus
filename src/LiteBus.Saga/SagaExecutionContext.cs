using System.Collections.Concurrent;
using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Ambient saga scope used by the inbox processor hook during one envelope dispatch.
/// </summary>
/// <remarks>
///     Scope state is keyed by saga correlation so parallel dispatches with distinct correlations do not share mutable
///     state. An instance <see cref="AsyncLocal{T}" /> re-attaches the active correlation within one asynchronous flow
///     because values set after an awaited hook call do not flow to the host processor's continuation.
/// </remarks>
public sealed class SagaExecutionContext : ISagaContext
{
    /// <summary>
    ///     The active dispatch scopes keyed by normalized saga correlation.
    /// </summary>
    private readonly ConcurrentDictionary<string, Scope> _scopes = new(StringComparer.Ordinal);

    /// <summary>
    ///     The storage key for the active dispatch scope on the current asynchronous execution flow.
    /// </summary>
    private readonly AsyncLocal<string?> _ambientScopeKey = new();

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
        if (GetActiveScope()?.State is null)
        {
            throw new InvalidOperationException("No saga scope is active for the current dispatch.");
        }

        return (TState)GetActiveScope()!.State;
    }

    /// <inheritdoc />
    public void SetState<TState>(TState state)
        where TState : class, new()
    {
        ArgumentNullException.ThrowIfNull(state);

        if (GetActiveScope() is null)
        {
            throw new InvalidOperationException("No saga scope is active for the current dispatch.");
        }

        GetActiveScope()!.State = state;
        GetActiveScope()!.IsDirty = true;
    }

    /// <inheritdoc />
    public void Complete()
    {
        if (GetActiveScope() is null)
        {
            throw new InvalidOperationException("No saga scope is active for the current dispatch.");
        }

        GetActiveScope()!.ShouldComplete = true;
    }

    /// <summary>
    ///     Begins a saga scope for one correlation and state snapshot.
    /// </summary>
    /// <param name="correlation">The saga correlation.</param>
    /// <param name="state">The current state object.</param>
    /// <param name="version">The optimistic lock version.</param>
    internal void Begin(SagaCorrelation correlation, object state, int version)
    {
        var scopeKey = SagaCorrelationKey.BuildStorageKey(correlation);
        _scopes[scopeKey] = new Scope(correlation, state, version);
        _ambientScopeKey.Value = scopeKey;
    }

    /// <summary>
    ///     Re-attaches a previously started dispatch scope for one correlation on the current asynchronous flow.
    /// </summary>
    /// <param name="correlation">The saga correlation.</param>
    /// <returns>
    ///     <see langword="true" /> when a scope exists for the correlation; otherwise, <see langword="false" />.
    /// </returns>
    internal bool TryAttach(SagaCorrelation correlation)
    {
        ArgumentNullException.ThrowIfNull(correlation);

        var scopeKey = SagaCorrelationKey.BuildStorageKey(correlation);

        if (!_scopes.ContainsKey(scopeKey))
        {
            return false;
        }

        _ambientScopeKey.Value = scopeKey;
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

        if (GetActiveScope() is null)
        {
            throw new InvalidOperationException("No saga scope is active for the current dispatch.");
        }

        GetActiveScope()!.State = state;
        GetActiveScope()!.Version = version;
    }

    /// <summary>
    ///     Clears the active saga scope for the current asynchronous execution flow.
    /// </summary>
    internal void Reset()
    {
        var scopeKey = _ambientScopeKey.Value;

        _ambientScopeKey.Value = null;

        if (scopeKey is not null)
        {
            _scopes.TryRemove(scopeKey, out _);
        }
    }

    /// <summary>
    ///     Captures handler mutations that must survive an optimistic concurrency reload.
    /// </summary>
    /// <returns>The shadow snapshot for the active scope.</returns>
    internal DispatchShadow CaptureDispatchShadow()
    {
        if (GetActiveScope() is null)
        {
            throw new InvalidOperationException("No saga scope is active for the current dispatch.");
        }

        var scope = GetActiveScope()!;

        return new DispatchShadow(
            scope.State,
            scope.IsDirty,
            scope.ShouldComplete);
    }

    /// <summary>
    ///     Replaces the active state object after a concurrency reload.
    /// </summary>
    /// <param name="state">The handler-owned state snapshot to persist.</param>
    internal void ReapplyState(object state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (GetActiveScope() is null)
        {
            throw new InvalidOperationException("No saga scope is active for the current dispatch.");
        }

        GetActiveScope()!.State = state;
        GetActiveScope()!.IsDirty = true;
    }

    /// <summary>
    ///     Gets the active state object without a generic constraint.
    /// </summary>
    /// <returns>The active state object.</returns>
    internal object GetActiveState()
    {
        if (GetActiveScope()?.State is null)
        {
            throw new InvalidOperationException("No saga scope is active for the current dispatch.");
        }

        return GetActiveScope()!.State;
    }

    /// <summary>
    ///     Gets the active scope for the current asynchronous execution flow.
    /// </summary>
    /// <returns>The active scope when one is attached; otherwise, <see langword="null" />.</returns>
    private Scope? GetActiveScope()
    {
        if (_ambientScopeKey.Value is { } scopeKey && _scopes.TryGetValue(scopeKey, out var attachedScope))
        {
            return attachedScope;
        }

        if (_scopes.Count == 1)
        {
            foreach (var pair in _scopes)
            {
                _ambientScopeKey.Value = pair.Key;
                return pair.Value;
            }
        }

        return null;
    }

    /// <summary>
    ///     Handler mutations captured before an optimistic concurrency reload.
    /// </summary>
    /// <param name="State">The state object observed at capture time.</param>
    /// <param name="IsDirty">A value indicating whether the handler called <see cref="SetState{TState}" />.</param>
    /// <param name="ShouldComplete">A value indicating whether the handler called <see cref="Complete" />.</param>
    internal readonly record struct DispatchShadow(object State, bool IsDirty, bool ShouldComplete);

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
