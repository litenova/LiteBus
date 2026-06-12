using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.MediationStrategies;
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
            new MessageMediationRequest<IQuery<TQueryResult>, Task<TQueryResult>>
            {
                MessageMediationStrategy = mediationStrategy,
                MessageResolveStrategy = resolveStrategy,
                Tags = ResolveTags(queryMediationSettings),
                Items = queryMediationSettings.Items,
                HandlerPredicate = ResolveHandlerPredicate(queryMediationSettings)
            },
            cancellationToken);
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
            new MessageMediationRequest<IStreamQuery<TQueryResult>, IAsyncEnumerable<TQueryResult>>
            {
                MessageMediationStrategy = mediationStrategy,
                MessageResolveStrategy = resolveStrategy,
                Tags = ResolveTags(queryMediationSettings),
                Items = queryMediationSettings.Items,
                HandlerPredicate = ResolveHandlerPredicate(queryMediationSettings)
            },
            cancellationToken);
    }

    /// <summary>
    ///     Resolves mediation tags from routing settings with legacy filter fallback.
    /// </summary>
    /// <param name="settings">The query mediation settings supplied by the caller.</param>
    /// <returns>The tag collection applied during mediation.</returns>
    private static IEnumerable<string> ResolveTags(QueryMediationSettings settings)
    {
        var routingTags = settings.Routing.Tags.ToList();
        return routingTags.Count > 0 ? routingTags : settings.Filters.Tags;
    }

    /// <summary>
    ///     Resolves the handler predicate from routing settings with legacy filter fallback.
    /// </summary>
    /// <param name="settings">The query mediation settings supplied by the caller.</param>
    /// <returns>The predicate applied after tag filtering.</returns>
    private static Func<IHandlerDescriptor, bool> ResolveHandlerPredicate(QueryMediationSettings settings)
    {
        return descriptor => settings.Routing.HandlerPredicate(descriptor) && settings.Filters.HandlerPredicate(descriptor);
    }
}
