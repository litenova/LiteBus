using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions;
using LiteBus.Messaging.Registry.Abstractions;
using LiteBus.Messaging.Registry.Descriptors;

namespace LiteBus.Messaging.Registry.Builders;

public sealed class PreHandlerDescriptorBuilder : IHandlerDescriptorBuilder
{
    public bool CanBuild(Type type)
    {
        return type.IsAssignableTo(typeof(IMessagePreHandler));
    }

    public IEnumerable<IHandlerDescriptor> Build(Type handlerType)
    {
        var interfaces = handlerType.GetInterfacesEqualTo(typeof(IMessagePreHandler<>));
        var priority = handlerType.GetPriorityFromAttribute();
        var tags = handlerType.GetTagsFromAttribute();

        foreach (var @interface in interfaces)
        {
            var messageType = @interface.GetGenericArguments()[0];

            yield return new PreHandlerDescriptor
            {
                MessageType = messageType.IsGenericType ? messageType.GetGenericTypeDefinition() : messageType,
                Priority = priority,
                Tags = tags,
                HandlerType = handlerType
            };
        }
    }
}