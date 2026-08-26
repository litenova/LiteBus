using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions;
using LiteBus.Messaging.Registry.Abstractions;
using LiteBus.Messaging.Registry.Descriptors;

namespace LiteBus.Messaging.Registry.Builders;

/// <summary>
///     Discovers <see cref="IPreHandlerDescriptor" /> instances from pre-handler types.
/// </summary>
public sealed class PreHandlerDescriptorBuilder : IHandlerDescriptorBuilder
{
    /// <inheritdoc />
    public bool CanBuild(Type type)
    {
        return type.IsAssignableTo(typeof(IMessagePreHandler));
    }

    /// <inheritdoc />
    public IEnumerable<IHandlerDescriptor> Build(Type type)
    {
        // A pre-handler declares its message type through either contract. Both dispatch through IMessagePreHandler,
        // so both produce the same descriptor kind, but the message type must be read from whichever it implements.
        var interfaces = type.GetInterfacesEqualTo(typeof(IMessagePreHandler<>))
            .Concat(type.GetInterfacesEqualTo(typeof(IShortCircuitingPreHandler<>)));

        var priority = type.GetPriorityFromAttribute();
        var tags = type.GetTagsFromAttribute();

        foreach (var @interface in interfaces)
        {
            var messageType = @interface.GetGenericArguments()[0];

            yield return new PreHandlerDescriptor
            {
                MessageType = messageType.NormalizeMessageRegistrationType(),
                Priority = priority,
                Tags = tags,
                HandlerType = type
            };
        }
    }
}
