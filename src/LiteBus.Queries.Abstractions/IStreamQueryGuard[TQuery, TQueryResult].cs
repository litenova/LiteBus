using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a pre-handler that decides whether a stream query is permitted to run, and can hand the caller a
///     refusal stream.
/// </summary>
/// <typeparam name="TQuery">The specific stream query type this guard runs for.</typeparam>
/// <typeparam name="TQueryResult">The item type of the stream the query produces.</typeparam>
/// <remarks>
///     This contract is opt-in. <see cref="IQueryGuard{TQuery}" /> covers a stream query too, and refuses by raising
///     <see cref="LiteBusMessageDeniedException" />. The value a stream query produces is the stream itself, so the
///     refusal value here is typed over <see cref="IAsyncEnumerable{T}" />.
/// </remarks>
public interface IStreamQueryGuard<in TQuery, TQueryResult> : IMessageGuard<TQuery, IAsyncEnumerable<TQueryResult>>
    where TQuery : IStreamQuery<TQueryResult>;
