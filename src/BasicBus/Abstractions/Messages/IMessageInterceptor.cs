using System.Threading.Tasks;

namespace BasicBus.Abstractions
{
    /// <summary>
    /// The base of all interceptors
    /// </summary>
    public interface IMessageInterceptor<in TMessage, TMessageResult> where TMessage : IMessage<TMessageResult>
    {
        Task OnPreHandleAsync(TMessage message);
        
        Task OnPostHandleAsync(TMessage message);
    }
    
    public interface IMessageInterceptor<in TMessage> where TMessage : IMessage
    {
        Task OnPreHandleAsync(TMessage message);
        
        Task OnPostHandleAsync(TMessage message);
    }
}