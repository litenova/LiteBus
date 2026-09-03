using System.Runtime.CompilerServices;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Testing;

/// <summary>
///     Returns configured results for queries sent through <see cref="IQueryMediator" />.
/// </summary>
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
    ///     Gets or sets the result returned for the next query.
    /// </summary>
    public object? NextResult { get; set; }

    /// <summary>
    ///     Gets the queries recorded since construction or the last <see cref="Clear" /> call.
    /// </summary>
    public IReadOnlyList<object> Queries => _queries;

    /// <summary>
    ///     Gets the queries evaluated since construction or the last <see cref="Clear" /> call.
    /// </summary>
    /// <value>
    ///     Separate from <see cref="Queries" />, because an evaluation asks whether a read is permitted and does not
    ///     perform it.
    /// </value>
    public IReadOnlyList<object> Evaluated => _evaluated;

    /// <inheritdoc />
    public Task<TQueryResult> QueryAsync<TQueryResult>(
        IQuery<TQueryResult> query,
        QueryMediationSettings? queryMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        _queries.Add(query);
        return Task.FromResult((TQueryResult) (NextResult ?? default(TQueryResult)!));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(
        IStreamQuery<TQueryResult> query,
        QueryMediationSettings? queryMediationSettings = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        _queries.Add(query);

        if (NextResult is TQueryResult typedResult)
        {
            yield return typedResult;
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
        _queries.Add(query);

        return Task.FromResult(
            MediationResult<TQueryResult>.Succeeded((TQueryResult) (NextResult ?? default(TQueryResult)!)));
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
        _evaluated.Add(query);
        return Task.FromResult(MediationDecision.Allowed);
    }

    /// <summary>
    ///     Clears recorded queries and resets the next result.
    /// </summary>
    public void Clear()
    {
        _queries.Clear();
        _evaluated.Clear();
        NextResult = null;
    }
}
