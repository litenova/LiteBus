using System;
using System.Threading;
using System.Threading.Tasks;
#nullable enable

namespace Paykan.Messaging.Abstractions
{
    public interface IMessageMediator
    {
        /// <summary>
        /// Sends a message to its handlers
        /// </summary>
        /// <param name="message">The message to send</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <typeparam name="TMessage">The type of message to send</typeparam>
        /// <returns>A task representing the execution of handlers</returns>
        Task SendAsync<TMessage>(TMessage message,
                                 CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a message to the first corresponding handler
        /// </summary>
        /// <param name="message">The message to send</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <typeparam name="TMessage">The type of message to send</typeparam>
        /// <typeparam name="TMessageResult">The type of message result</typeparam>
        /// <returns>The message result</returns>
        TMessageResult SendAsync<TMessage, TMessageResult>(TMessage message,
                                                           CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a message to the first corresponding handler
        /// </summary>
        /// <param name="message">The message to send</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <typeparam name="TMessageResult">The type of message result</typeparam>
        /// <returns>The message result</returns>
        TMessageResult SendAsync<TMessageResult>(object message,
                                                 CancellationToken cancellationToken = default);
    }
}