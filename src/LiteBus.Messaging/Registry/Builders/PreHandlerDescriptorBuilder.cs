using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions;
using LiteBus.Messaging.Registry.Abstractions;
using LiteBus.Messaging.Registry.Descriptors;

namespace LiteBus.Messaging.Registry.Builders;

/// <summary>
///     Discovers <see cref="IPreHandlerDescriptor" /> instances from pre-handler and gate types.
/// </summary>
public sealed class PreHandlerDescriptorBuilder : IHandlerDescriptorBuilder
{
    /// <inheritdoc />
    public bool CanBuild(Type type)
    {
        return type.IsAssignableTo(typeof(IMessagePreHandler));
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    public IEnumerable<IHandlerDescriptor> Build(Type type)
    {
        // A pre-handler declares its message type through any of three contracts: a plain pre-handler, a gate over a
        // message with no result, or a gate over a message with one. All three produce the same descriptor kind, and
        // the contract recorded here is what the pipeline later dispatches through.
        var interfaces = type.GetInterfacesEqualTo(typeof(IMessagePreHandler<>))
            .Concat(type.GetInterfacesEqualTo(typeof(IMessageGate<>)))
            .Concat(type.GetInterfacesEqualTo(typeof(IMessageGate<,>)));

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
                HandlerType = type,
                ContractType = @interface,
                Dispatch = PipelineDispatch.For(@interface)
            };
        }
    }
}
