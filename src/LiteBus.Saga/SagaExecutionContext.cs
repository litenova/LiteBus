using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Ambient saga scope used by the inbox processor hook during one envelope dispatch.
/// </summary>
/// <remarks>
///     Scope state is stored on the singleton instance registered for <see cref="ISagaContext" />.
///     Hosts must keep inbox <c>DispatcherConcurrency</c> at 1 until per-dispatch scope isolation ships.
/// </remarks>
public sealed class SagaExecutionContext : ISagaContext
{
    /// <summary>
    ///     The active dispatch scope for this context instance.
    /// </summary>
    private Scope? _currentScope;

    /// <summary>
    ///     Gets a value indicating whether the saga should be marked completed after dispatch.
    /// </summary>
    internal bool ShouldComplete => _currentScope?.ShouldComplete ?? false;

    /// <summary>
    ///     Gets a value indicating whether state changed during dispatch.
    /// </summary>
    internal bool IsDirty => _currentScope?.IsDirty ?? false;

    /// <summary>
    ///     Gets the optimistic lock version observed when the saga was loaded.
    /// </summary>
    internal int Version => _currentScope?.Version ?? 0;

    /// <inheritdoc />
    public bool IsActive => _currentScope is not null;

    /// <inheritdoc />
    public SagaCorrelation? Correlation => _currentScope?.Correlation;

    /// <inheritdoc />
    public TState GetState<TState>()
        where TState : class, new()
    {
        if (_currentScope?.State is null)
        {
            throw new InvalidOperationException("No saga scope is active for the current dispatch.");
        }

        return (TState) _currentScope.State;
    }

    /// <inheritdoc />
    public void SetState<TState>(TState state)
        where TState : class, new()
    {
        ArgumentNullException.ThrowIfNull(state);

        if (_currentScope is null)
        {
            throw new InvalidOperationException("No saga scope is active for the current dispatch.");
        }

        _currentScope.State = state;
        _currentScope.IsDirty = true;
    }

    /// <inheritdoc />
    public void Complete()
    {
        if (_currentScope is null)
        {
            throw new InvalidOperationException("No saga scope is active for the current dispatch.");
        }

        _currentScope.ShouldComplete = true;
    }

    /// <summary>
    ///     Begins a saga scope for one correlation and state snapshot.
    /// </summary>
    /// <param name="correlation">The saga correlation.</param>
    /// <param name="state">The current state object.</param>
    /// <param name="version">The optimistic lock version.</param>
    internal void Begin(SagaCorrelation correlation, object state, int version)
    {
        _currentScope = new Scope(correlation, state, version);
    }

    /// <summary>
    ///     Clears the active saga scope.
    /// </summary>
    internal void Reset()
    {
        _currentScope = null;
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
