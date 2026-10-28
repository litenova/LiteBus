using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Testing;

/// <summary>
///     Returns configured results for queries sent through <see cref="IQueryMediator" />.
/// </summary>
/// <remarks>
///     All members are thread-safe. Recording properties return snapshots, so a held reference never observes later
///     queries and mutating it never affects the recorded history.
/// </remarks>
public sealed class TestQueryMediator : IQueryMediator
{
    /// <summary>
    ///     Gets queries recorded by <see cref="QueryAsync" /> overloads.
    /// </summary>
    private readonly List<object> _queries = [];

    /// <summary>
    ///     Queries recorded by <see cref="EvaluateAsync" />, kept apart because evaluating is not executing.
    /// </summary>
    private readonly List<object> _evaluated = [];

    /// <summary>
    ///     Gets the lock that serializes recording mutations, snapshot reads, and result stub consumption.
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    ///     The stubbed result consumed by the next query, stream, or tried query.
    /// </summary>
    private object? _nextResult;

    /// <summary>
    ///     Gets or sets the result returned for the next query.
    /// </summary>
    /// <value>
    ///     The stub is single-use: each <see cref="QueryAsync" />, <see cref="StreamAsync" />, and
    ///     <see cref="TryQueryAsync" /> call consumes it and resets the property to <see langword="null" />, so a
    ///     test that forgets to stub the second query observes a default result instead of a stale stub. A stub
    ///     whose runtime type does not match the query result type throws <see cref="InvalidCastException" />.
    /// </value>
    public object? NextResult
    {
        get
        {
            lock (_sync)
            {
                return _nextResult;
            }
        }

        set
        {
            lock (_sync)
            {
                _nextResult = value;
            }
        }
    }

    /// <summary>
    ///     Gets a snapshot of the queries recorded since construction or the last <see cref="Clear" /> call.
    /// </summary>
    public IReadOnlyList<object> Queries
    {
        get
        {
            lock (_sync)
            {
                return [.. _queries];
            }
        }
    }

    /// <summary>
    ///     Gets a snapshot of the queries evaluated since construction or the last <see cref="Clear" /> call.
    /// </summary>
    /// <value>
    ///     Separate from <see cref="Queries" />, because an evaluation asks whether a read is permitted and does not
    ///     perform it.
    /// </value>
    public IReadOnlyList<object> Evaluated
    {
        get
        {
            lock (_sync)
            {
                return [.. _evaluated];
            }
        }
    }

    /// <inheritdoc />
    public Task<TQueryResult> QueryAsync<TQueryResult>(
        IQuery<TQueryResult> query,
        QueryMediationSettings? queryMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        object? stub;

        lock (_sync)
        {
            _queries.Add(query);
            stub = TakeNextResult();
        }

        return Task.FromResult((TQueryResult) (stub ?? default(TQueryResult)!));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(
        IStreamQuery<TQueryResult> query,
        QueryMediationSettings? queryMediationSettings = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        object? stub;

        lock (_sync)
        {
            _queries.Add(query);
            stub = TakeNextResult();
        }

        if (stub is not null)
        {
            yield return (TQueryResult) stub;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<MediationResult<TQueryResult>> TryQueryAsync<TQueryResult>(
        IQuery<TQueryResult> query,
        QueryMediationSettings? queryMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        object? stub;

        lock (_sync)
        {
            _queries.Add(query);
            stub = TakeNextResult();
        }

        return Task.FromResult(
            MediationResult<TQueryResult>.Succeeded((TQueryResult) (stub ?? default(TQueryResult)!)));
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Records the query and permits it. A recording double runs no guards, so it can only answer that nothing
    ///     objected; assert on a real pipeline when the decision itself is what is under test.
    /// </remarks>
    public Task<MediationDecision> EvaluateAsync(
        IQuery query,
        QueryMediationSettings? queryMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        lock (_sync)
        {
            _evaluated.Add(query);
        }

        return Task.FromResult(MediationDecision.Allowed);
    }

    /// <summary>
    ///     Clears recorded queries and resets the next result.
    /// </summary>
    public void Clear()
    {
        lock (_sync)
        {
            _queries.Clear();
            _evaluated.Clear();
            _nextResult = null;
        }
    }

    /// <summary>
    ///     Consumes the stubbed result and resets it to <see langword="null" />.
    /// </summary>
    /// <returns>The stubbed result, or <see langword="null" /> when no stub was set.</returns>
    /// <remarks>
    ///     Callers must hold the mutation lock; the stub field mutates in place.
    /// </remarks>
    private object? TakeNextResult()
    {
        var stub = _nextResult;
        _nextResult = null;
        return stub;
    }
}
