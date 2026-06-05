using System.Collections.Concurrent;
using LiteBus.Messaging.Abstractions;
using LiteBus.Saga.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Thread-safe in-memory saga store for unit tests and local development.
/// </summary>
public sealed class InMemorySagaStore : ISagaStore
{
    /// <summary>
    ///     One persisted saga row.
    /// </summary>
    private sealed record SagaRow(string StateJson, int Version, bool IsCompleted);

    /// <summary>
    ///     The serializer used to convert state objects to JSON.
    /// </summary>
    private readonly IMessageSerializer _serializer;

    /// <summary>
    ///     The saga rows keyed by correlation and saga type.
    /// </summary>
    private readonly ConcurrentDictionary<string, SagaRow> _rows = new(StringComparer.Ordinal);

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemorySagaStore" /> class.
    /// </summary>
    /// <param name="serializer">The serializer used to convert state objects to JSON.</param>
    public InMemorySagaStore(IMessageSerializer serializer)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    /// <inheritdoc />
    public async Task<SagaInstance<TState>?> LoadAsync<TState>(
        SagaCorrelation correlation,
        CancellationToken cancellationToken = default)
        where TState : class, new()
    {
        ArgumentNullException.ThrowIfNull(correlation);

        if (!_rows.TryGetValue(BuildKey(correlation), out var row))
        {
            return null;
        }

        var state = await _serializer.DeserializeAsync(typeof(TState), row.StateJson, cancellationToken).ConfigureAwait(false);
        return new SagaInstance<TState>
        {
            Correlation = correlation,
            State = (TState)state,
            Version = row.Version,
            IsCompleted = row.IsCompleted
        };
    }

    /// <inheritdoc />
    public async Task SaveAsync<TState>(
        SagaCorrelation correlation,
        TState state,
        int expectedVersion,
        CancellationToken cancellationToken = default)
        where TState : class, new()
    {
        ArgumentNullException.ThrowIfNull(correlation);
        ArgumentNullException.ThrowIfNull(state);

        var key = BuildKey(correlation);
        var stateJson = await _serializer.SerializeAsync(state, cancellationToken).ConfigureAwait(false);

        _rows.AddOrUpdate(
            key,
            _ => new SagaRow(stateJson, 1, false),
            (_, existing) =>
            {
                if (existing.Version != expectedVersion)
                {
                    throw new SagaConcurrencyException(correlation);
                }

                return new SagaRow(stateJson, existing.Version + 1, existing.IsCompleted);
            });
    }

    /// <inheritdoc />
    public Task CompleteAsync(SagaCorrelation correlation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(correlation);

        var key = BuildKey(correlation);
        _rows.AddOrUpdate(
            key,
            _ => new SagaRow("{}", 1, true),
            (_, existing) => existing with { IsCompleted = true });

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Builds the storage key for one saga correlation.
    /// </summary>
    /// <param name="correlation">The saga correlation.</param>
    /// <returns>The composite storage key.</returns>
    private static string BuildKey(SagaCorrelation correlation)
        => $"{correlation.SagaType}:{correlation.CorrelationId}";
}
