using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries;

/// <summary>
///     The primary implementation of <see cref="IQueryMediator" />. It orchestrates the query execution
///     pipeline for immediate, in-process query handling.
/// </summary>
public sealed class QueryMediator : IQueryMediator
{
    /// <summary>
    ///     Gets the core message mediator used to execute the query pipeline.
    /// </summary>
    private readonly IMessageMediator _messageMediator;

    /// <summary>
    ///     Initializes a new instance of the <see cref="QueryMediator" /> class.
    /// </summary>
    /// <param name="messageMediator">The core message mediator for immediate query execution.</param>
    public QueryMediator(IMessageMediator messageMediator)
    {
        ArgumentNullException.ThrowIfNull(messageMediator);

        _messageMediator = messageMediator;
    }

    /// <inheritdoc />
    public Task<TQueryResult> QueryAsync<TQueryResult>(IQuery<TQueryResult> query,
                                                       QueryMediationSettings? queryMediationSettings = null,
                                                       CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        queryMediationSettings ??= new QueryMediationSettings();
        var mediationStrategy = new SingleAsyncHandlerMediationStrategy<IQuery<TQueryResult>, TQueryResult>();
        var resolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        return _messageMediator.Mediate(query,
            new MediateOptions<IQuery<TQueryResult>, Task<TQueryResult>>
            {
                MessageMediationStrategy = mediationStrategy,
                MessageResolveStrategy = resolveStrategy,
                CancellationToken = cancellationToken,
                Tags = queryMediationSettings.Filters.Tags,
                Items = queryMediationSettings.Items
            });
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query,
                                                                    QueryMediationSettings? queryMediationSettings = null,
                                                                    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        queryMediationSettings ??= new QueryMediationSettings();
        var mediationStrategy = new SingleStreamHandlerMediationStrategy<IStreamQuery<TQueryResult>, TQueryResult>(cancellationToken);
        var resolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        return _messageMediator.Mediate(query,
            new MediateOptions<IStreamQuery<TQueryResult>, IAsyncEnumerable<TQueryResult>>
            {
                MessageMediationStrategy = mediationStrategy,
                MessageResolveStrategy = resolveStrategy,
                CancellationToken = cancellationToken,
                Tags = queryMediationSettings.Filters.Tags,
                Items = queryMediationSettings.Items
            });
    }
}