using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Extensions;
using LiteBus.Messaging.Internal.Registry.Abstractions;
using LiteBus.Messaging.Internal.Registry.Descriptors;

namespace LiteBus.Messaging.Internal.Registry.Builders;

internal sealed class ErrorHandlerDescriptorBuilder : IHandlerDescriptorBuilder
{
    public bool CanBuild(Type type)
    {
        return type.IsAssignableTo(typeof(IMessageErrorHandler));
    }

    public IEnumerable<IHandlerDescriptor> Build(Type handlerType)
    {
        var interfaces = handlerType.GetInterfacesEqualTo(typeof(IMessageErrorHandler<,>));
        var order = handlerType.GetOrderFromAttribute();

        foreach (var @interface in interfaces)
        {
            var messageType = @interface.GetGenericArguments()[0];
            var messageResultType = @interface.GetGenericArguments()[1];
            var tags = handlerType.GetTagsFromAttribute();

            yield return new ErrorHandlerDescriptor
            {
                MessageType = messageType.IsGenericType ? messageType.GetGenericTypeDefinition() : messageType,
                MessageResultType = messageResultType,
                Order = order,
                Tags = tags,
                HandlerType = handlerType
            };
        }
    }
}