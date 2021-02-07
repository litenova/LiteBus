using System;
using System.Diagnostics;
using System.Threading;

namespace Paykan.Messaging.Abstractions.Extensions
{
    public static class HandlerWrapperExtensions
    {
        /// <summary>
        ///     Allows a handler to handle a given message in a non-generic way
        /// </summary>
        /// <param name="handlerObject">The handler object</param>
        /// <param name="message">The given message</param>
        /// <param name="messageResultType">The result type of given message</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The message result</returns>
        /// <exception cref="NotSupportedException">Is thrown if the handler object is not a valid handler type</exception>
        public static object HandleAsync(this object handlerObject,
                                         object message,
                                         Type messageResultType,
                                         CancellationToken cancellationToken)
        {
            if (!handlerObject.GetType().IsAssignableTo(typeof(IMessageHandler)))
                throw new
                    NotSupportedException($"The given {nameof(handlerObject)} is not a valid {nameof(IMessageHandler)}");

            var genericHandlerWrapperType = typeof(GenericMessageHandlerWrapper<,>)
                .MakeGenericType(message.GetType(), messageResultType);

            var handler = (MessageHandlerWrapper) Activator.CreateInstance(genericHandlerWrapperType);

            Debug.Assert(handler != null, nameof(handler) + " != null");

            return handler.HandleAsync(message, handlerObject, cancellationToken);
        }

        /// <summary>
        ///     Allows a handler to handle a given message without providing the message type
        /// </summary>
        /// <param name="handlerObject">The handler object</param>
        /// <param name="message">The given message</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The message result</returns>
        /// <exception cref="NotSupportedException">Is thrown if the handler object is not a valid handler type</exception>
        public static TMessageResult HandleAsync<TMessageResult>(this object handlerObject,
                                                                 object message,
                                                                 CancellationToken cancellationToken)
        {
            if (!handlerObject.GetType().IsAssignableTo(typeof(IMessageHandler)))
                throw new
                    NotSupportedException($"The given {nameof(handlerObject)} is not a valid {nameof(IMessageHandler)}");

            var genericHandlerWrapperType = typeof(GenericMessageHandlerWrapper<,>)
                .MakeGenericType(message.GetType(), typeof(TMessageResult));

            var handler = (MessageHandlerWrapper) Activator.CreateInstance(genericHandlerWrapperType);

            Debug.Assert(handler != null, nameof(handler) + " != null");

            return (TMessageResult) handler.HandleAsync(message, handlerObject, cancellationToken);
        }
    }
}