using System.Runtime.CompilerServices;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Testing;

/// <summary>
///     Returns configured results for queries sent through <see cref="IQueryMediator" />.
/// </summary>
public sealed class TestQueryMediator : IQueryMediator, IRegistrableQueryConstruct
{
    /// <summary>
    ///     Gets queries recorded by <see cref="QueryAsync" /> overloads.
    /// </summary>
    private readonly List<object> _queries = [];

    /// <summary>
    ///     Gets or sets the result returned for the next query.
    /// </summary>
    public object? NextResult { get; set; }

    /// <summary>
    ///     Gets the queries recorded since construction or the last <see cref="Clear" /> call.
    /// </summary>
    public IReadOnlyList<object> Queries => _queries;

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

    /// <summary>
    ///     Clears recorded queries and resets the next result.
    /// </summary>
    public void Clear()
    {
        _queries.Clear();
        NextResult = null;
    }
}