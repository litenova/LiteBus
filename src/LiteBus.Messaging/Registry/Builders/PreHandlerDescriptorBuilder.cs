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
///     Discovers <see cref="IPreHandlerDescriptor" /> instances from pre-handler, guard, and shortcut types.
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
        // A pre-handler declares its message type through one of five contracts: a plain pre-handler, a guard with or
        // without a refusal value, or a shortcut over a message with or without a result. All five produce the same
        // descriptor kind, and the contract recorded here decides both the stage that runs the handler and the closed
        // contract the pipeline later dispatches through. One class may implement several, and each yields its own
        // descriptor, so a class that both refuses and answers is run once per stage.
        var interfaces = type.GetInterfacesEqualTo(typeof(IMessagePreHandler<>))
            .Concat(type.GetInterfacesEqualTo(typeof(IMessageGuard<>)))
            .Concat(type.GetInterfacesEqualTo(typeof(IMessageGuard<,>)))
            .Concat(type.GetInterfacesEqualTo(typeof(IMessageShortcut<>)))
            .Concat(type.GetInterfacesEqualTo(typeof(IMessageShortcut<,>)));

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
                Stage = PipelineDispatch.StageFor(@interface),
                Dispatch = PipelineDispatch.For(@interface)
            };
        }
    }
}
