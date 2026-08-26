using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a pre-handler that decides whether a stream query reaches its handler.
/// </summary>
/// <typeparam name="TQuery">The specific stream query type this gate runs for.</typeparam>
/// <typeparam name="TQueryResult">The item type of the stream the query produces.</typeparam>
/// <remarks>
///     The value a stream query produces is the stream itself, so the directive is typed over
///     <see cref="IAsyncEnumerable{T}" />. A short-circuit that supplies no stream is a legitimate answer and means the
///     caller enumerates nothing.
/// </remarks>
public interface IStreamQueryGate<in TQuery, TQueryResult> : IMessageGate<TQuery, IAsyncEnumerable<TQueryResult>>
    where TQuery : IStreamQuery<TQueryResult>;
