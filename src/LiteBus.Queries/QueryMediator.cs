using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Queries;

/// <inheritdoc cref="IQueryMediator" />
public sealed class QueryMediator : IQueryMediator
{
    private readonly IMessageMediator _messageMediator;

    public QueryMediator(IMessageMediator messageMediator)
    {
        _messageMediator = messageMediator;
    }

    public Task<TQueryResult?> QueryAsync<TQueryResult>(IQuery<TQueryResult> query,
                                                        QueryMediationSettings? queryMediationSettings = null,
                                                        CancellationToken cancellationToken = default)
    {
        queryMediationSettings ??= new QueryMediationSettings();
        var mediationStrategy = new SingleAsyncHandlerMediationStrategy<IQuery<TQueryResult>, TQueryResult?>();
        var resolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        return _messageMediator.Mediate(query,
            new MediateOptions<IQuery<TQueryResult>, Task<TQueryResult?>>
            {
                MessageMediationStrategy = mediationStrategy,
                MessageResolveStrategy = resolveStrategy,
                CancellationToken = cancellationToken,
                Tags = queryMediationSettings.Filters.Tags
            });
    }

    public IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(IStreamQuery<TQueryResult> query,
                                                                    QueryMediationSettings? queryMediationSettings = null,
                                                                    CancellationToken cancellationToken = default)
    {
        queryMediationSettings ??= new QueryMediationSettings();
        var mediationStrategy = new SingleStreamHandlerMediationStrategy<IStreamQuery<TQueryResult>, TQueryResult>(cancellationToken);
        var resolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        return _messageMediator.Mediate(query,
            new MediateOptions<IStreamQuery<TQueryResult>, IAsyncEnumerable<TQueryResult>>
            {
                MessageMediationStrategy = mediationStrategy,
                MessageResolveStrategy = resolveStrategy,
                CancellationToken = cancellationToken,
                Tags = queryMediationSettings.Filters.Tags
            });
    }
}