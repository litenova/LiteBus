using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Mutable saga scope used by the inbox processor hook during one envelope dispatch.
/// </summary>
public sealed class SagaExecutionContext : ISagaContext, ISaga<object>
{
    /// <summary>
    ///     The active correlation for this scope.
    /// </summary>
    private SagaCorrelation? _correlation;

    /// <summary>
    ///     The active state object for this scope.
    /// </summary>
    private object? _state;

    /// <summary>
    ///     The optimistic lock version observed when the saga was loaded.
    /// </summary>
    private int _version;

    /// <summary>
    ///     Gets a value indicating whether the saga should be marked completed after dispatch.
    /// </summary>
    internal bool ShouldComplete { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether state changed during dispatch.
    /// </summary>
    internal bool IsDirty { get; private set; }

    /// <inheritdoc />
    public bool IsActive => _correlation is not null;

    /// <inheritdoc />
    public SagaCorrelation? Correlation => _correlation;

    /// <inheritdoc />
    object ISaga<object>.State
    {
        get => GetState<object>();
        set => SetState(value);
    }

    /// <summary>
    ///     Gets the optimistic lock version observed when the saga was loaded.
    /// </summary>
    internal int Version => _version;

    /// <inheritdoc />
    public TState GetState<TState>()
        where TState : class, new()
    {
        if (_state is null)
        {
            throw new InvalidOperationException("No saga scope is active for the current dispatch.");
        }

        return (TState)_state;
    }

    /// <inheritdoc />
    public void SetState<TState>(TState state)
        where TState : class, new()
    {
        ArgumentNullException.ThrowIfNull(state);

        if (_correlation is null)
        {
            throw new InvalidOperationException("No saga scope is active for the current dispatch.");
        }

        _state = state;
        IsDirty = true;
    }

    /// <inheritdoc />
    public void Complete()
    {
        if (_correlation is null)
        {
            throw new InvalidOperationException("No saga scope is active for the current dispatch.");
        }

        ShouldComplete = true;
    }

    /// <summary>
    ///     Begins a saga scope for one correlation and state snapshot.
    /// </summary>
    /// <param name="correlation">The saga correlation.</param>
    /// <param name="state">The current state object.</param>
    /// <param name="version">The optimistic lock version.</param>
    internal void Begin(SagaCorrelation correlation, object state, int version)
    {
        _correlation = correlation;
        _state = state;
        _version = version;
        ShouldComplete = false;
        IsDirty = false;
    }

    /// <summary>
    ///     Clears the active saga scope.
    /// </summary>
    internal void Reset()
    {
        _correlation = null;
        _state = null;
        _version = 0;
        ShouldComplete = false;
        IsDirty = false;
    }
}
