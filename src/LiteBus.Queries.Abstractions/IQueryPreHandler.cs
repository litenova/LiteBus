using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions;

public interface IQueryPreHandlerBase : IQueryConstruct
{
}

/// <summary>
///     Represents an action that is executed on each query pre-handle phase
/// </summary>
public interface IQueryPreHandler : IQueryPreHandlerBase, IMessagePreHandler<IQueryBase>
{
}

/// <summary>
///     Represents an action that is executed on <typeparamref cref="TQuery" /> pre-handle phase
/// </summary>
public interface IQueryPreHandler<in TQuery> : IQueryPreHandlerBase, IMessagePreHandler<TQuery>
    where TQuery : IQueryBase
{
}