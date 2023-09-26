using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

/// <summary>
///     Represents an action that is executed on <typeparamref cref="TQuery" /> pre-handle phase
/// </summary>
public interface IQueryPreHandler<in TQuery> : IRegistrableQueryConstruct, IAsyncMessagePreHandler<TQuery> where TQuery : IQuery
{
}