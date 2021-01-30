using System;
using System.Threading;
using System.Threading.Tasks;
#nullable enable

namespace Paykan.Messaging.Abstractions
{
    public interface IMessageMediator
    {
        /// <summary>
        /// Sends a message to its corresponding handlers
        /// </summary>
        /// <param name="message">The message to send</param>
        /// <param name="config">The publish configuration</param>
        /// <typeparam name="TMessage">The type of message to send</typeparam>
        /// <returns>A task representing the execution of handlers</returns>
        Task SendAsync<TMessage>(TMessage message, Action<IPublishConfiguration> config);

        /// <summary>
        /// Sends a message to its handler
        /// </summary>
        /// <param name="message">The message to send</param>
        /// <param name="config">The send configuration</param>
        /// <typeparam name="TMessage">The type of message to send</typeparam>
        /// <typeparam name="TMessageResult">The type of message result</typeparam>
        /// <returns>The message result</returns>
        TMessageResult Send<TMessage, TMessageResult>(TMessage message, Action<ISendConfiguration> config);
    }
}