using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions
{
    public interface IMediationStrategy
    {
        TMessageResult Mediate<TMessage, TMessageResult>(TMessage message);
    }
    
    public interface IMessageMediator
    {
        TMessageResult Mediate<TMessage, TMessageResult>(TMessage message, IMediationStrategy strategy);

        /// <summary>
        /// Sends a message to its corresponding handler. If 
        /// </summary>
        /// <param name="message">The message</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <typeparam name="TMessage">the message type</typeparam>
        /// <typeparam name="TMessageResult">The message result type</typeparam>
        /// <returns>The <typeparamref name="TMessageResult"/></returns>
        // TMessageResult SendAsync<TMessage, TMessageResult>(TMessage message,
        //                                                    CancellationToken cancellationToken);
        //
        // Task SendAsync<TMessage>(TMessage message,
        //                          CancellationToken cancellationToken);
        //
        // IAsyncEnumerable<TMessageResult> StreamAsync<TMessage, TMessageResult>(TMessage message,
        //                                                                        CancellationToken cancellationToken);
        //
        // TMessageResult Send<TMessage, TMessageResult>(TMessage message);
        //
        // void Send<TMessage>(TMessage message);
    }
}