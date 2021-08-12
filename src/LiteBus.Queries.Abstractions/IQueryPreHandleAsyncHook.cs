using LiteBus.Messaging.Abstractions;

namespace LiteBus.Queries.Abstractions
{
    /// <summary>
    ///     Represents an action that is executed on each query pre-handle phase
    /// </summary>
    public interface IQueryPreHandleAsyncHook : IPreHandleAsyncHook<IQueryBase>
    {
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref cref="TQuery" /> pre-handle phase
    /// </summary>
    public interface IQueryPreHandleAsyncHook<in TQuery> : IPreHandleAsyncHook<TQuery> where TQuery : IQueryBase
    {
    }
}