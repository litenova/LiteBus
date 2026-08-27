using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a pre-handler that decides whether a query is permitted to run.
/// </summary>
/// <typeparam name="TQuery">The specific query type this guard runs for.</typeparam>
/// <remarks>
///     A refusal does not owe the caller the value the handler would have produced, so this contract fits every query,
///     including a stream query. The mediation reports <see cref="MediationOutcome.Denied" />, an audit trail records a
///     denial, and the refusal reaches the caller as <see cref="LiteBusMessageDeniedException" />. Serving a query from
///     a cache is a different decision and belongs to <see cref="IQueryShortcut{TQuery,TQueryResult}" />, which the
///     framework runs only after every guard has allowed the query.
/// </remarks>
public interface IQueryGuard<in TQuery> : IMessageGuard<TQuery>
    where TQuery : IQuery;
