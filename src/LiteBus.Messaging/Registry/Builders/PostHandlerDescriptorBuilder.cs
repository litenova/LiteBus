using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions;
using LiteBus.Messaging.Registry.Abstractions;
using LiteBus.Messaging.Registry.Descriptors;

namespace LiteBus.Messaging.Registry.Builders;

public sealed class PostHandlerDescriptorBuilder : IHandlerDescriptorBuilder
{
    public bool CanBuild(Type type)
    {
        return type.IsAssignableTo(typeof(IMessagePostHandler));
    }

    public IEnumerable<IHandlerDescriptor> Build(Type handlerType)
    {
        var interfaces = handlerType.GetInterfacesEqualTo(typeof(IMessagePostHandler<,>));
        var priority = handlerType.GetPriorityFromAttribute();
        var tags = handlerType.GetTagsFromAttribute();

        foreach (var @interface in interfaces)
        {
            var messageType = @interface.GetGenericArguments()[0];
            var messageResultType = @interface.GetGenericArguments()[1];

            yield return new PostHandlerDescriptor
            {
                MessageType = messageType.IsGenericType ? messageType.GetGenericTypeDefinition() : messageType,
                MessageResultType = messageResultType,
                Priority = priority,
                Tags = tags,
                HandlerType = handlerType
            };
        }
    }
}