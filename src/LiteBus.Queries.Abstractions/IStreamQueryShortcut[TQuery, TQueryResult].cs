using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a pre-handler that answers a stream query without running its handler.
/// </summary>
/// <typeparam name="TQuery">The specific stream query type this shortcut runs for.</typeparam>
/// <typeparam name="TQueryResult">The item type of the stream the query produces.</typeparam>
/// <remarks>
///     The value a stream query produces is the stream itself, so the answer is typed over
///     <see cref="IAsyncEnumerable{T}" />. Supplying a stream yields that stream instead of the handler's;
///     <see cref="Shortcut{TQueryResult}.Skip" /> supplies none, which is a legitimate answer and means the caller
///     enumerates nothing.
/// </remarks>
public interface IStreamQueryShortcut<in TQuery, TQueryResult>
    : IMessageShortcut<TQuery, IAsyncEnumerable<TQueryResult>>
    where TQuery : IStreamQuery<TQueryResult>;
