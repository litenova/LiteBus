using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Turns a refused stream query into the stream the caller enumerates.
/// </summary>
/// <typeparam name="TQuery">The stream query type this mapper covers.</typeparam>
/// <typeparam name="TQueryResult">The item type of the stream the query produces.</typeparam>
/// <remarks>
///     The value a stream query produces is the stream itself, so the mapping is typed over
///     <see cref="IAsyncEnumerable{T}" />. Returning an empty sequence hands the caller a refusal it enumerates as no
///     items; without a mapper the refusal reaches the caller as an exception instead.
/// </remarks>
public interface IStreamQueryRefusalMapper<in TQuery, out TQueryResult>
    : IMessageRefusalMapper<TQuery, IAsyncEnumerable<TQueryResult>>
    where TQuery : IStreamQuery<TQueryResult>;
