using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions;
using LiteBus.Messaging.Registry.Abstractions;
using LiteBus.Messaging.Registry.Descriptors;

namespace LiteBus.Messaging.Registry.Builders;

/// <summary>
///     Discovers <see cref="ICompletionHandlerDescriptor" /> instances from completion handler types.
/// </summary>
public sealed class CompletionHandlerDescriptorBuilder : IHandlerDescriptorBuilder
{
    /// <inheritdoc />
    public bool CanBuild(Type type)
    {
        return type.IsAssignableTo(typeof(IMessageCompletionHandler));
    }

    /// <inheritdoc />
    public IEnumerable<IHandlerDescriptor> Build(Type type)
    {
        var interfaces = type.GetInterfacesEqualTo(typeof(IMessageCompletionHandler<>));
        var priority = type.GetPriorityFromAttribute();
        var tags = type.GetTagsFromAttribute();

        foreach (var @interface in interfaces)
        {
            var messageType = @interface.GetGenericArguments()[0];

            yield return new CompletionHandlerDescriptor
            {
                MessageType = messageType.NormalizeMessageRegistrationType(),
                Priority = priority,
                Tags = tags,
                HandlerType = type
            };
        }
    }
}
