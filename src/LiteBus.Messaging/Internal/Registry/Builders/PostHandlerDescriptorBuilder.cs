using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Extensions;
using LiteBus.Messaging.Internal.Registry.Abstractions;
using LiteBus.Messaging.Internal.Registry.Descriptors;

namespace LiteBus.Messaging.Internal.Registry.Builders;

public sealed class PostHandlerDescriptorBuilder : IDescriptorBuilder<IPostHandlerDescriptor>
{
    public bool CanBuild(Type type)
    {
        return type.IsAssignableTo(typeof(IMessagePostHandler));
    }

    public IEnumerable<IPostHandlerDescriptor> Build(Type handlerType)
    {
        var interfaces = handlerType.GetInterfacesEqualTo(typeof(IMessagePostHandler<,>));
        var order = handlerType.GetOrderFromAttribute();

        foreach (var @interface in interfaces)
        {
            var messageType = @interface.GetGenericArguments()[0];
            var messageResultType = @interface.GetGenericArguments()[1];

            yield return new PostHandlerDescriptor(handlerType, messageType, messageResultType, order);
        }
    }
}