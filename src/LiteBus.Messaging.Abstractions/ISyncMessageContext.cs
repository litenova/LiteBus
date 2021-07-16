using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions
{
    public interface ISyncMessageContext<TMessage> : IMessageContext<TMessage, VoidMessageResult>
    {
    }

    public interface ISyncMessageContext<TMessage, TMessageResult> : IMessageContext<TMessage, TMessageResult>
    {
        
    }
}