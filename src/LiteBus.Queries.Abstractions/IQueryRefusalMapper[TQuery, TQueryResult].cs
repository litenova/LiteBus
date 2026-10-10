using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Turns a refused query into the result the caller expects.
/// </summary>
/// <typeparam name="TQuery">The query type this mapper covers.</typeparam>
/// <typeparam name="TQueryResult">The type of result the query produces.</typeparam>
/// <remarks>
///     Register this against <see cref="IQuery" /> to cover every query that produces
///     <typeparamref name="TQueryResult" />, or against a concrete query to override that for one message. Without a
///     mapper, a refusal reaches the caller as <see cref="LiteBusMessageDeniedException" /> or
///     <see cref="LiteBusMessageInvalidException" />.
/// </remarks>
public interface IQueryRefusalMapper<in TQuery, out TQueryResult> : IMessageRefusalMapper<TQuery, TQueryResult>
    where TQuery : IQuery;
