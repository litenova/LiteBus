using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
/// Represents a post-handler that executes after a specific stream query of type <typeparamref name="TQuery" /> is processed,
/// with access to the query's result stream of type <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
/// <typeparam name="TQuery">The specific stream query type this post-handler targets.</typeparam>
/// <typeparam name="TQueryResult">The type of elements in the result stream.</typeparam>
/// <remarks>
/// <para>
/// These post-handlers execute after a stream query handler has produced a result stream and the consumer has
/// fully enumerated it. They have access to both the original query and the result stream object, allowing for
/// operations such as logging, metrics, or other processing that depends on the query outcome.
/// </para>
/// <para>
/// <strong>Warning:</strong> The <paramref name="messageResult"/> stream will have already been consumed by the time
/// this handler executes. It should not be re-enumerated unless its underlying implementation supports it (e.g., if it's backed by an in-memory list).
/// To pass data from the stream handler to the post-handler, use the <see cref="IExecutionContext.Items"/> collection.
/// </para>
/// </remarks>
public interface IStreamQueryPostHandler<in TQuery, in TQueryResult> : IRegistrableQueryConstruct,
                                                                       IAsyncMessagePostHandler<TQuery, IAsyncEnumerable<TQueryResult>>
    where TQuery : IStreamQuery<TQueryResult>;