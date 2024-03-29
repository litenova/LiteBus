using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents an action that is executed on <typeparamref cref="TQuery" /> post-handle phase
/// </summary>
public interface IQueryPostHandler<in TQuery> : IRegistrableQueryConstruct, IAsyncMessagePostHandler<TQuery> where TQuery : IQuery
{
}