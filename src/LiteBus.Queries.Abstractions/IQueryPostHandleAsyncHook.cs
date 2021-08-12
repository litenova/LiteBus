using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions
{
    /// <summary>
    ///     Represents an action that is executed on each query post-handle phase
    /// </summary>
    public interface IQueryPostHandleAsyncHook : IPostHandleAsyncHook<IQueryBase>
    {
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref cref="TQuery" /> post-handle phase
    /// </summary>
    public interface IQueryPostHandleAsyncHook<in TQuery> : IPostHandleAsyncHook<TQuery> where TQuery : IQueryBase
    {
    }
}