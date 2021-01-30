using Paykan.Messaging.Abstractions;

namespace Paykan.Abstractions.Interceptors
{
    public interface IQueryPostHandleHook : IPostHandleHook<IBaseQuery>
    {
        
    }
    
    public interface IQueryPostHandleHook<in TQuery> : IPostHandleHook<TQuery> where TQuery : IBaseQuery
    {
        
    }

}