using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions
{
    public interface IMessageMediator
    {
        TMessageResult Mediate<TMessage, TMessageResult>(TMessage message,
                                                         IMessageResolveStrategy<TMessage> messageResolveStrategy,
                                                         IMessageMediationStrategy<TMessage, TMessageResult> messageMediationStrategy);
    }
}