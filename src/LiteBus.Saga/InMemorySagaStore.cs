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
    ///     The saga rows keyed by tenant, definition, and correlation.
    /// </summary>
    private readonly ConcurrentDictionary<string, SagaRow> _rows = new(StringComparer.Ordinal);

    /// <summary>
    ///     The serializer used to convert state objects to JSON.
    /// </summary>
    private readonly IMessageSerializer _serializer;

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

        if (!_rows.TryGetValue(SagaCorrelationKey.BuildStorageKey(correlation), out var row))
        {
            return null;
        }

        var state = await _serializer.DeserializeAsync(typeof(TState), row.StateJson, cancellationToken).ConfigureAwait(false);

        return new SagaInstance<TState>
        {
            Correlation = correlation,
            State = (TState) state,
            Version = row.Version,
            IsCompleted = row.IsCompleted
        };
    }

    /// <inheritdoc />
    public async Task SaveAsync<TState>(
        SagaSaveItem<TState> item,
        CancellationToken cancellationToken = default)
        where TState : class, new()
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.State);

        var key = SagaCorrelationKey.BuildStorageKey(item.Correlation);
        var stateJson = await _serializer.SerializeAsync(item.State, cancellationToken).ConfigureAwait(false);

        _rows.AddOrUpdate(
            key,
            _ => new SagaRow(stateJson, 1, false),
            (_, existing) =>
            {
                if (item.ExpectedVersion == 0)
                {
                    throw new SagaConcurrencyException(item.Correlation);
                }

                if (existing.Version != item.ExpectedVersion)
                {
                    throw new SagaConcurrencyException(item.Correlation);
                }

                return new SagaRow(stateJson, existing.Version + 1, existing.IsCompleted);
            });
    }

    /// <inheritdoc />
    public Task CompleteAsync(SagaCompleteItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var key = SagaCorrelationKey.BuildStorageKey(item.Correlation);

        if (!_rows.TryGetValue(key, out var existing))
        {
            throw new SagaConcurrencyException(item.Correlation);
        }

        if (existing.Version != item.ExpectedVersion)
        {
            throw new SagaConcurrencyException(item.Correlation);
        }

        _rows[key] = existing with { IsCompleted = true, Version = existing.Version + 1 };

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SagaInstanceSummary>> QueryAsync(
        SagaQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var results = _rows
            .Where(pair => MatchesQuery(pair.Key, pair.Value, filter))
            .Take(filter.Take)
            .Select(pair => ToSummary(pair.Key, pair.Value))
            .ToList();

        return Task.FromResult<IReadOnlyList<SagaInstanceSummary>>(results);
    }

    /// <inheritdoc />
    public Task<int> PurgeAsync(SagaPurgeFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var removed = 0;

        foreach (var key in _rows.Keys.ToArray())
        {
            if (!_rows.TryGetValue(key, out var row) || !MatchesPurge(key, row, filter))
            {
                continue;
            }

            if (_rows.TryRemove(key, out _))
            {
                removed++;
            }
        }

        return Task.FromResult(removed);
    }

    /// <summary>
    ///     Determines whether one row matches the query filter.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="row">The saga row.</param>
    /// <param name="filter">The query filter.</param>
    /// <returns><see langword="true" /> when the row matches.</returns>
    private static bool MatchesQuery(string key, SagaRow row, SagaQueryFilter filter)
    {
        return MatchesCorrelationKey(key, filter.TenantId, filter.SagaDefinitionId, filter.CorrelationId) &&
               (filter.IsCompleted is null || row.IsCompleted == filter.IsCompleted);
    }

    /// <summary>
    ///     Determines whether one row matches the purge filter.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="row">The saga row.</param>
    /// <param name="filter">The purge filter.</param>
    /// <returns><see langword="true" /> when the row matches.</returns>
    private static bool MatchesPurge(string key, SagaRow row, SagaPurgeFilter filter)
    {
        if (!MatchesCorrelationKey(key, filter.TenantId, filter.SagaDefinitionId, filter.CorrelationId))
        {
            return false;
        }

        if (filter.IsCompleted is not null && row.IsCompleted != filter.IsCompleted)
        {
            return false;
        }

        return filter.CompletedBefore is null || row.IsCompleted;
    }

    /// <summary>
    ///     Determines whether one storage key matches optional correlation filters.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="tenantId">The optional tenant filter.</param>
    /// <param name="sagaDefinitionId">The optional saga definition filter.</param>
    /// <param name="correlationId">The optional correlation filter.</param>
    /// <returns><see langword="true" /> when the key matches.</returns>
    private static bool MatchesCorrelationKey(
        string key,
        string? tenantId,
        string? sagaDefinitionId,
        string? correlationId)
    {
        var parts = key.Split(':', 3);

        if (parts.Length != 3)
        {
            return false;
        }

        if (tenantId is not null &&
            !string.Equals(parts[0], SagaCorrelationKey.NormalizeTenantId(tenantId), StringComparison.Ordinal))
        {
            return false;
        }

        if (sagaDefinitionId is not null && !string.Equals(parts[1], sagaDefinitionId, StringComparison.Ordinal))
        {
            return false;
        }

        return correlationId is null || string.Equals(parts[2], correlationId, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Maps one in-memory row to a query summary.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <param name="row">The saga row.</param>
    /// <returns>The query summary.</returns>
    private static SagaInstanceSummary ToSummary(string key, SagaRow row)
    {
        var parts = key.Split(':', 3);

        return new SagaInstanceSummary
        {
            Correlation = new SagaCorrelation
            {
                TenantId = string.IsNullOrEmpty(parts[0]) ? null : parts[0],
                SagaDefinitionId = parts[1],
                CorrelationId = parts[2]
            },
            Version = row.Version,
            IsCompleted = row.IsCompleted
        };
    }

    /// <summary>
    ///     One persisted saga row.
    /// </summary>
    private sealed record SagaRow(string StateJson, int Version, bool IsCompleted);
}
