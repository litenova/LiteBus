using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions;
using LiteBus.Messaging.Registry.Abstractions;
using LiteBus.Messaging.Registry.Descriptors;

namespace LiteBus.Messaging.Registry.Builders;

/// <summary>
///     Discovers <see cref="IMainHandlerDescriptor" /> instances from main handler types.
/// </summary>
public sealed class HandlerDescriptorBuilder : IHandlerDescriptorBuilder
{
    /// <inheritdoc />
    public bool CanBuild(Type type)
    {
        return type.IsAssignableTo(typeof(IMessageHandler));
    }

    /// <inheritdoc />
    public IEnumerable<IHandlerDescriptor> Build(Type type)
    {
        var interfaces = type.GetInterfacesEqualTo(typeof(IMessageHandler<,>));

        var priority = type.GetPriorityFromAttribute();

        foreach (var @interface in interfaces)
        {
            var messageType = @interface.GetGenericArguments()[0];
            var messageResultType = @interface.GetGenericArguments()[1];
            var tags = type.GetTagsFromAttribute();

            yield return new MainHandlerDescriptor
            {
                MessageType = messageType.IsGenericType ? messageType.GetGenericTypeDefinition() : messageType,
                MessageResultType = messageResultType,
                Priority = priority,
                Tags = tags,
                HandlerType = type
            };
        }
    }
}