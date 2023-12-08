using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Extensions;
using LiteBus.Messaging.Internal.Registry.Abstractions;
using LiteBus.Messaging.Internal.Registry.Descriptors;

namespace LiteBus.Messaging.Internal.Registry.Builders;

public sealed class HandlerDescriptorBuilder : IHandlerDescriptorBuilder
{
    public bool CanBuild(Type type)
    {
        return type.IsAssignableTo(typeof(IMessageHandler));
    }

    public IEnumerable<IHandlerDescriptor> Build(Type handlerType)
    {
        var interfaces = handlerType.GetInterfacesEqualTo(typeof(IMessageHandler<,>));

        var order = handlerType.GetOrderFromAttribute();

        foreach (var @interface in interfaces)
        {
            var messageType = @interface.GetGenericArguments()[0];
            var messageResultType = @interface.GetGenericArguments()[1];

            yield return new MainHandlerDescriptor
            {
                MessageType = messageType.IsGenericType ? messageType.GetGenericTypeDefinition() : messageType,
                MessageResultType = messageResultType,
                Order = order,
                Tags = ArraySegment<string>.Empty,
                HandlerType = handlerType
            };
        }
    }
}