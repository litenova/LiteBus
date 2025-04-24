using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents a validator that performs validation on a specific query type <typeparamref name="TQuery" /> before
///     it is processed.
/// </summary>
/// <typeparam name="TQuery">The specific query type this validator targets.</typeparam>
/// <remarks>
///     IQueryValidator is a specialized wrapper around IQueryPreHandler that simplifies implementing validation logic.
///     It provides a more semantic API through the ValidateAsync method while internally mapping to the PreHandleAsync
///     method.
///     Query validators run before the main query handler and can be used to ensure queries meet business rules
///     and contain valid data before processing. Multiple validators can be registered for each query type.
///     If validation fails, implementations should throw an exception to prevent the query from being processed further.
/// </remarks>
public interface IQueryValidator<in TQuery> : IQueryPreHandler<TQuery> where TQuery : IQuery
{
    Task IAsyncMessagePreHandler<TQuery>.PreHandleAsync(TQuery message, CancellationToken cancellationToken)
    {
        return ValidateAsync(message, cancellationToken);
    }

    /// <summary>
    ///     Validates the query
    /// </summary>
    /// <param name="query">The query to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the asynchronous operation</returns>
    Task ValidateAsync(TQuery query, CancellationToken cancellationToken = default);
}