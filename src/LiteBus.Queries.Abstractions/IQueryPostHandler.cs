using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions
{
    public interface IQueryPostHandlerBase
    {
    }

    /// <summary>
    ///     Represents an action that is executed on each query post-handle phase
    /// </summary>
    public interface IQueryPostHandler : IQueryPostHandlerBase, IMessagePostHandler<IQueryBase>
    {
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref cref="TQuery" /> post-handle phase
    /// </summary>
    public interface IQueryPostHandler<in TQuery> : IQueryPostHandlerBase, IMessagePostHandler<TQuery>
        where TQuery : IQueryBase
    {
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref cref="TQuery" /> post-handle phase
    /// </summary>
    public interface IQueryPostHandler<in TQuery, in TQueryResult> : IQueryPostHandlerBase,
                                                                     IMessagePostHandler<TQuery, TQueryResult>
        where TQuery : IQuery<TQueryResult>
    {
    }
}