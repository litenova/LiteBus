using System;
using System.Collections.Generic;
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
                Tags = queryMediationSettings.Routing.Tags,
                Items = queryMediationSettings.Items,
                HandlerPredicate = queryMediationSettings.Routing.HandlerPredicate
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
                Tags = queryMediationSettings.Routing.Tags,
                Items = queryMediationSettings.Items,
                HandlerPredicate = queryMediationSettings.Routing.HandlerPredicate
            },
            cancellationToken);
    }
    /// <inheritdoc />
    public async Task<MediationResult<TQueryResult>> TryQueryAsync<TQueryResult>(
        IQuery<TQueryResult> query,
        QueryMediationSettings? queryMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        queryMediationSettings ??= new QueryMediationSettings();
        var capture = new MediationEndingCapture();

        var request = new MessageMediationRequest<IQuery<TQueryResult>, Task<TQueryResult>>
        {
            MessageMediationStrategy = new SingleAsyncHandlerMediationStrategy<IQuery<TQueryResult>, TQueryResult>(),
            MessageResolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy(),
            Tags = queryMediationSettings.Routing.Tags,
            Items = WithEndingCapture(queryMediationSettings.Items, capture),
            HandlerPredicate = queryMediationSettings.Routing.HandlerPredicate
        };

        try
        {
            var value = await _messageMediator.Mediate(query, request, cancellationToken).ConfigureAwait(false);

            // A registered refusal mapper returns a value rather than raising, so the outcome comes from the capture.
            return MediationResultFactory.FromCapture(capture, value, hasValue: true);
        }
        catch (Exception exception) when (MediationExceptionFilters.IsRefusal(exception))
        {
            return MediationResultFactory.FromCapture<TQueryResult>(capture, value: default, hasValue: false);
        }
    }

    /// <inheritdoc />
    public Task<MediationDecision> EvaluateAsync(
        IQuery query,
        QueryMediationSettings? queryMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        queryMediationSettings ??= new QueryMediationSettings();

        return _messageMediator.Mediate(query,
            new MessageMediationRequest<IQuery, Task<MediationDecision>>
            {
                MessageMediationStrategy = new DecisionEvaluationMediationStrategy<IQuery>(),
                MessageResolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy(),
                Tags = queryMediationSettings.Routing.Tags,
                Items = queryMediationSettings.Items,
                HandlerPredicate = queryMediationSettings.Routing.HandlerPredicate
            },
            cancellationToken);
    }

    /// <summary>
    ///     Copies the caller's items and adds the ending capture the strategy fills in.
    /// </summary>
    /// <param name="items">The items the caller supplied.</param>
    /// <param name="capture">The capture to install.</param>
    /// <returns>The items to pass to the mediator.</returns>
    /// <remarks>
    ///     Copied rather than mutated, because a settings object reused across calls would otherwise leave one
    ///     mediation writing into a previous call's result.
    /// </remarks>
    private static Dictionary<string, object> WithEndingCapture(
        IDictionary<string, object> items,
        MediationEndingCapture capture)
    {
        return new Dictionary<string, object>(items, StringComparer.Ordinal)
        {
            [MediationEndingCapture.ItemKey] = capture
        };
    }
}
