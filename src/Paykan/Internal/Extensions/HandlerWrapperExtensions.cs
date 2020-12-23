using System;
using System.Diagnostics;
using System.Threading;

namespace Paykan.Internal.Extensions
{
    internal static class HandlerWrapperExtensions
    {
        public static object HandleAsync(this object handlerObject,
                                                 object message,
                                                 Type messageResultType,
                                                 CancellationToken cancellationToken)
        {
            var genericHandlerWrapperType = typeof(GenericHandlerWrapper<,>)
                .MakeGenericType(message.GetType(), messageResultType);

            var handler = (HandlerWrapper) Activator.CreateInstance(genericHandlerWrapperType);

            return handler.HandleAsync(message, handlerObject, cancellationToken);
        }
        
        public static TMessageResult HandleAsync<TMessageResult>(this object handlerObject,
                                                                 object message,
                                                                 CancellationToken cancellationToken)
        {
            var genericHandlerWrapperType = typeof(GenericHandlerWrapper<,>)
                .MakeGenericType(message.GetType(), typeof(TMessageResult));

            var handler = (HandlerWrapper) Activator.CreateInstance(genericHandlerWrapperType);

            return (TMessageResult)handler.HandleAsync(message, handlerObject, cancellationToken);
        }
    }
}