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
///     <see cref="IAsyncEnumerable{T}" />, and answering yields that stream instead of the handler's. Answering with
///     <c>AsyncEnumerable.Empty&lt;TQueryResult&gt;()</c> is how a shortcut says the caller enumerates nothing, which
///     states that outright rather than leaving it implied by a missing value.
/// </remarks>
public interface IStreamQueryShortcut<in TQuery, TQueryResult>
    : IMessageShortcut<TQuery, IAsyncEnumerable<TQueryResult>>
    where TQuery : IStreamQuery<TQueryResult>;
