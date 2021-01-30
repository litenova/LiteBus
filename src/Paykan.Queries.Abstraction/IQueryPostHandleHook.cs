namespace Paykan.Queries.Abstraction
{
    public interface IQueryPostHandleHook : IPostHandleHook<IBaseQuery>
    {
        
    }
    
    public interface IQueryPostHandleHook<in TQuery> : IPostHandleHook<TQuery> where TQuery : IBaseQuery
    {
        
    }

}