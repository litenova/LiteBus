using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions;
using LiteBus.Messaging.Registry.Abstractions;
using LiteBus.Messaging.Registry.Descriptors;

namespace LiteBus.Messaging.Registry.Builders;

/// <summary>
///     Discovers <see cref="IRefusalMapperDescriptor" /> instances from refusal mapper types.
/// </summary>
public sealed class RefusalMapperDescriptorBuilder : IHandlerDescriptorBuilder
{
    /// <inheritdoc />
    public bool CanBuild(Type type)
    {
        return type.IsAssignableTo(typeof(IMessageRefusalMapper));
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    public IEnumerable<IHandlerDescriptor> Build(Type type)
    {
        // One class may map refusals for several messages, and each closed contract yields its own descriptor so the
        // pipeline dispatches through the one that matches the message being refused.
        var interfaces = type.GetInterfacesEqualTo(typeof(IMessageRefusalMapper<,>));
        var priority = type.GetPriorityFromAttribute();
        var tags = type.GetTagsFromAttribute();

        foreach (var @interface in interfaces)
        {
            var messageType = @interface.GetGenericArguments()[0];
            var messageResultType = @interface.GetGenericArguments()[1];

            yield return new RefusalMapperDescriptor
            {
                MessageType = messageType.NormalizeMessageRegistrationType(),
                MessageResultType = messageResultType,
                Priority = priority,
                Tags = tags,
                HandlerType = type,
                ContractType = @interface,
                Dispatch = PipelineDispatch.For(@interface)
            };
        }
    }
}
